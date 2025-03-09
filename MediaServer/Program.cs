using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;

partial class MediaServer
{
    private static Config _config = null!;
    private static string _configPath = null!;

    /// <summary>
    /// Main entry point for the program
    /// </summary>
    /// <param name="args"></param>
    static async Task Main(string[] args)
    {
        // Check if args was a path to a config file
        if (args.Length > 0)
        {
            _configPath = args[0];
        }
        else
        {
            // Get the users appdata directory
            string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            baseDirectory = Path.Combine(baseDirectory, "MediaServer");
            _configPath = Path.Combine(baseDirectory, "config.json");
        }

        Console.WriteLine("Config Path: " + _configPath);
        _config = LoadConfig(_configPath);
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add($"http://{_config.Interface}:{_config.Port}/");
        listener.Start();
        Console.WriteLine($"Server running at http://{_config.Interface}:{_config.Port}/");

        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequest(context));
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>
    /// Checks if a file is a symlink (so it can be handled correctly)
    /// </summary>
    /// <param name="path"></param>
    /// <returns>True if Symlink</returns>
    private static bool IsSymlink(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            return fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Print Request Info
        // IP Address (Including Domain Name if Possible), URL.
        Console.WriteLine($"{request.RemoteEndPoint.Address} - {request.Url}");
        if (request.HttpMethod == HttpMethod.Get.Method)
        {
            try
            {
                if (request.Url != null && request.Url.LocalPath.EndsWith("/"))
                {
                    await HandleDirectoryListing(request, response);
                }
                else
                {
                    await ServeFile(request, response);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                response.StatusCode = 500;
                response.Close();
            }
        }

        if (request.HttpMethod == HttpMethod.Post.Method)
        {
            if (request.Url.AbsolutePath.EndsWith("/uploadFile"))
            {
                await HandleFileUpload(request, response);
            }
        }

        response.StatusCode = 500;
        response.Close();
    }

    private static async Task HandleFileUpload(HttpListenerRequest request, HttpListenerResponse response)
    {
        // 1. Make sure they're authorized
        string authHeader = request.Headers["Authorization"] ?? string.Empty;
        if (string.IsNullOrEmpty(authHeader) || !IsAuthorized(authHeader))
        {
            response.StatusCode = 401;
            response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure Area\"");
            response.Close();
            return;
        }

        if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data"))
        {
            string boundary = request.ContentType.Split('=')[1];
            using var input = request.InputStream;
            using var reader = new StreamReader(input);

            string line;
            string filename = "";
            string filePath = "";
            string finalSavePath = "";

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Skip
                    continue;
                }
                if (line.Contains("Content-Disposition") && line.Contains("filename"))
                {
                    var filenamePart = line.Split(';')
                        .FirstOrDefault(x => x.Trim().StartsWith("filename=")) ?? "";
                    filename = filenamePart.Replace("filename=", "").Trim(' ', '\"');
                }
                if (line?.Contains("name=\"currentPath\"") == true)
                {
                    reader.ReadLine(); // Skip the empty line
                    var currentPath = reader.ReadLine()?.Trim() ?? "";
                    Console.WriteLine($"Uploading {filename} to {currentPath}");
                    filePath = currentPath.TrimStart('/');
                    filename = filename.TrimStart('/');
                }
            }

            finalSavePath = Path.Combine(_config.BaseDirectory, filePath, filename);
            finalSavePath = Path.GetFullPath(finalSavePath);
            Console.WriteLine(finalSavePath);
            if (!finalSavePath.StartsWith(_config.BaseDirectory))
            {
                Console.WriteLine(finalSavePath);
                response.StatusCode = 403;
                response.Close();
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalSavePath) ?? "");
            using var fileStream = File.Create(finalSavePath);
            // Move reader back to start
            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();

            bool writeData = false;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("--" + boundary))
                {
                    if (writeData) break;
                }
                else if (writeData)
                {
                    fileStream.Write(Encoding.UTF8.GetBytes(line + "\r\n"));
                }

                if (line.Contains("Content-Disposition") && line.Contains("filename"))
                {
                    while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                    {
                    }

                    writeData = true;
                }
            }

            response.StatusCode = 200;
            response.OutputStream.Write(Encoding.UTF8.GetBytes("File uploaded successfully"), 0, 26);
            response.Close();
        }
    }

    /// <summary>
    /// Generates a Directory Listing for the requested directory
    /// </summary>
    /// <param name="request"></param>
    /// <param name="response"></param>
    private static async Task HandleDirectoryListing(HttpListenerRequest request, HttpListenerResponse response)
    {
        string authHeader = request.Headers["Authorization"] ?? string.Empty;
        if (string.IsNullOrEmpty(authHeader) || !IsAuthorized(authHeader) || request.QueryString["logout"] == "401")
        {
            response.StatusCode = 401;
            response.AddHeader("WWW-Authenticate", "Basic realm=\"Secure Area\"");
            //Redirect to the same URL to clear the query string
            response.Close();
            return;
        }

        if (request.Url != null)
        {
            string directoryPath = Path.Combine(_config.BaseDirectory, request.Url.LocalPath.TrimStart('/'));
            directoryPath = Path.GetFullPath(directoryPath);
            if (!directoryPath.StartsWith(_config.BaseDirectory) || !Directory.Exists(directoryPath))
            {
                var buf = Encoding.UTF8.GetBytes("<html><body><h1>404 Not Found</h1></body></html>");
                response.ContentType = "text/html";
                response.ContentLength64 = buf.Length;
                await response.OutputStream.WriteAsync(buf, 0, buf.Length);
                response.StatusCode = 404;
                response.Close();
                return;
            }

            var files = Directory.GetFiles(directoryPath);
            var directories = Directory.GetDirectories(directoryPath);

            // Remove any Directories or Files that are Symlinks:
            files = files.Where(file => !IsSymlink(file)).ToArray();
            directories = directories.Where(dir => !IsSymlink(dir)).ToArray();
            // I would like to handle these correctly - By resolving them to the final directory or file and then displaying them.
            // But I'm not sure how to do that - The solutions I've seen seem to be very Windows centric.

            StringBuilder html = new StringBuilder("<!DOCTYPE html><html><head><style>");
            html.Append("table { width: 100%; border-collapse: collapse; }"); // Removed table-layout: fixed
            html.Append("th, td { padding: 3px; text-align: left; border: 1px solid #ddd; }");
            html.Append("th { background-color: #f2f2f2; }");
            html.Append("tr:nth-child(even) { background-color: #f9f9f9; }");
            html.Append("tr:hover { background-color: #f1f1f1; }");
            html.Append("td:nth-child(2), td:nth-child(3) { white-space: nowrap; }"); // Size and Time columns
            html.Append("td:first-child { word-break: break-all; width: 100%; }"); // Name column takes remaining space
            html.Append("</style></head><body>");
            html.Append("<h1>Directory Listing</h1>");
            // Add forms in the directory listing (inside HandleDirectoryListing)... But only if not default config
            if (_config.ShowNotification)
            {
                html.Append(
                    "<div class=\"notification\" style=\"background-color: #f44336; color: white; text-align: center; padding: 10px;\">");
                html.Append(
                    $"<p>Warning: You are using the default configuration. Edit the config at {Path.GetFullPath(_configPath)}</p>");
                html.Append("</div>");
            }
            else
            {
                html.Append("<form action='/uploadFile' method='POST' enctype='multipart/form-data'>");
                html.Append("<input type='file' name='uploadedFile'/>");
                html.Append("<input type='submit' value='Upload File'/>");
                html.Append($"<input type='hidden' name='currentPath' value='{request.Url.LocalPath}'/>");
                html.Append("</form>");
            }

            html.Append("<table border=\"1\"><tr><th>Name</th><th>Size</th><th>Last Modified</th></tr>");

            // Add a `..` link to go up a directory if we're not at the root
            if (request.Url.LocalPath.TrimEnd('/') != "")
            {
                html.Append(
                    $"<tr><td><button onclick=\"location.href='{request.Url.Scheme}://{request.Url.Authority}{request.Url.LocalPath.TrimEnd('/')}/../'\">Back</button></td><td>Directory</td><td>Tomorrow</td></tr>");
            }

            foreach (var dir in directories)
            {
                var dirInfo = new DirectoryInfo(dir);
                var dirName = WebUtility.HtmlEncode(dirInfo.Name);
                var dirUrl = Uri.EscapeDataString(dirInfo.Name);
                html.Append(
                    $"<tr><td><a href=\"{request.Url.Scheme}://{request.Url.Authority}{request.Url.LocalPath.TrimEnd('/')}/{dirUrl}/\">{dirName}/</a></td><td>Directory</td><td>{dirInfo.LastWriteTime:yyyy-MM-dd hh:mm:ss tt}</td></tr>");
            }

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var fileName = WebUtility.HtmlEncode(fileInfo.Name);
                var fileUrl = Uri.EscapeDataString(fileInfo.Name);
                // Decide if we should show the file size in KB, MB, GB
                string fileSize;
                if (fileInfo.Length < 1024)
                {
                    fileSize = $"{fileInfo.Length} B";
                }
                else if (fileInfo.Length < 1024 * 1024)
                {
                    fileSize = $"{fileInfo.Length / 1024} KB";
                }
                else if (fileInfo.Length < 1024 * 1024 * 1024)
                {
                    fileSize = $"{fileInfo.Length / 1024 / 1024} MB";
                }
                else
                {
                    fileSize = $"{fileInfo.Length / 1024 / 1024 / 1024} GB";
                }

                html.Append(
                    $"<tr><td><a href=\"{request.Url.Scheme}://{request.Url.Authority}{request.Url.LocalPath.TrimEnd('/')}/{fileUrl}\">{fileName}</a></td><td>{fileSize}</td><td>{fileInfo.LastWriteTime:yyyy-MM-dd hh:mm:ss tt}</td></tr>");
                //html.Append(
                //    $"<tr><td><a href=\"{request.Url.Scheme}://{request.Url.Authority}{request.Url.LocalPath.TrimEnd('/')}/{fileUrl}\">{fileName}</a></td><td>{fileInfo.Length / 1024 / 1024} MB</td><td>{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}</td></tr>");
            }

            html.Append("<p><a href=\"?logout=401\">Log out</a></p>");
            html.Append("</table></body></html>");
            byte[] buffer = Encoding.UTF8.GetBytes(html.ToString());
            response.ContentType = "text/html";
            //response.ContentLength64 = buffer.Length;
            await CompressAndWriteResponse(response, buffer);
        }

        //await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        //response.Close();
    }

    /// <summary>
    /// Generates a compressed response and writes it to the response stream
    /// </summary>
    /// <param name="response"></param>
    /// <param name="buffer"></param>
    private static async Task CompressAndWriteResponse(HttpListenerResponse response, byte[] buffer)
    {
        response.AddHeader("Content-Encoding", "gzip");
        await using (var gzipStream = new GZipStream(response.OutputStream, CompressionMode.Compress))
        {
            await gzipStream.WriteAsync(buffer, 0, buffer.Length);
        }

        response.Close();
    }

    /// <summary>
    /// Serves a File to the client, with support for Range Requests
    /// </summary>
    /// <param name="request"></param>
    /// <param name="response"></param>
    private static async Task ServeFile(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.Url != null)
        {
            string filePath = Path.Combine(_config.BaseDirectory, request.Url.LocalPath.TrimStart('/'));
            filePath = Path.GetFullPath(filePath);

            if (!filePath.StartsWith(_config.BaseDirectory) || !File.Exists(filePath))
            {
                var buf = Encoding.UTF8.GetBytes("<html><body><h1>404 Not Found</h1></body></html>");
                response.ContentType = "text/html";
                response.ContentLength64 = buf.Length;
                await response.OutputStream.WriteAsync(buf, 0, buf.Length);
                response.StatusCode = 404;
                response.Close();
                return;
            }

            FileInfo fileInfo = new FileInfo(filePath);

            // Get Filename without Path
            string fileName = Path.GetFileName(filePath);
            response.ContentType = GetMimeType(fileName);
            //response.ContentType = GetMimeType(filePath);
            response.ContentLength64 = fileInfo.Length;

            if (request.Headers["Range"] != null)
            {
                string rangeHeader = request.Headers["Range"] ?? "0";
                var range = rangeHeader.Replace("bytes=", "").Split('-');
                long start = long.Parse(range[0]);
                long end = range.Length > 1 && !string.IsNullOrEmpty(range[1])
                    ? long.Parse(range[1])
                    : fileInfo.Length - 1;

                if (start >= 0 && end >= start && end < fileInfo.Length)
                {
                    response.StatusCode = 206;
                    response.AddHeader("Content-Range", $"bytes {start}-{end}/{fileInfo.Length}");
                    response.ContentLength64 = end - start + 1;

                    await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Seek(start, SeekOrigin.Begin);
                        //1MB buffer size
                        byte[] buffer = new byte[1024 * 1024];
                        long bytesLeft = end - start + 1;
                        while (bytesLeft > 0)
                        {
                            int bytesRead = await fs.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, bytesLeft));
                            if (bytesRead <= 0) break;
                            try
                            {
                                await response.OutputStream.WriteAsync(buffer, 0, bytesRead);
                            }
                            catch (HttpListenerException ex) when (ex.ErrorCode == 64)
                            {
                                Console.WriteLine("Client disconnected");
                                break;
                            }
                            catch (Exception)
                            {
                                break;
                            }

                            bytesLeft -= bytesRead;
                        }
                    }

                    response.Close();
                    return;
                }
            }

            await using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await fs.CopyToAsync(response.OutputStream);
            }
        }

        response.Close();
    }

    /// <summary>
    /// Gets the MIME type of a file using the `file` command
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns>A correct mimetype, or application/octet-stream</returns>
    private static string? GetMimeType(string fileName)
    {
        string mimeType = "application/octet-stream";

        // There *must* be a better way to do this, instead of spawning a process for every new request

        try
        {
            // Create a Process
            ProcessStartInfo PSI = new ProcessStartInfo();
            PSI.FileName = "file";
            PSI.Arguments = $"--mime-type \"{fileName}\"";
            PSI.RedirectStandardOutput = true;
            PSI.UseShellExecute = false;
            PSI.CreateNoWindow = true;
            Process process = Process.Start(PSI);
            if (process != null)
            {
                process.WaitForExit();
                mimeType = process.StandardOutput.ReadToEnd().Trim();
            }

            // Return the mime type by splitting by space, and then getting the second part
            return mimeType.Split(' ')[1];
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        return mimeType;
    }

    /// <summary>
    /// Checks if the Authorization header is valid
    /// </summary>
    /// <param name="authHeader"></param>
    /// <returns>true if authorized</returns>
    private static bool IsAuthorized(string authHeader)
    {
        if (!authHeader.StartsWith("Basic ")) return false;

        // Reload the config on every request to allow for changes without restarting the server
        _config = LoadConfig(_configPath);

        string encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
        string decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
        string[] parts = decodedCredentials.Split(':');
        if (parts.Length != 2) return false;
        // Check if the user exists in the config and the password matches
        foreach (var user in _config.Users)
        {
            if (user.Key == parts[0] && user.Value == parts[1])
            {
                return true;
            }
        }

        return false;
    }
}