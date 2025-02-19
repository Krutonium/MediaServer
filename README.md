# MediaServer

## Overview

MediaServer is a simple HTTP server written in C# that serves files and directories from a specified base directory. It supports basic authentication, directory listings, and file downloads. The server also includes features such as GZip compression for responses and support for partial content (resume functionality).

To be clear, the files **Explicitly** do not have protection - The idea is to allow file serving without also having the directory listing visible - While still providing a directory listing for authenticated users to quickly and easily grab links.

## Features

- **Directory Listing**: Lists the contents of directories, including subdirectories and files.
- **File Download**: Allows downloading of files with support for partial content (resume functionality).
- **GZip Compression**: Compresses responses using GZip to reduce the size of data sent to the client. (Only Directory Listings)
- **Basic Authentication**: Secures access to the server using basic authentication.
- **Configurable**: Configuration options are stored in a JSON file, allowing for easy customization.

## Configuration

The server configuration is stored in a JSON file. If the configuration file does not exist, a default configuration will be generated. The configuration file includes the following options:

- `Users`: A dictionary of usernames and passwords for basic authentication.
- `BaseDirectory`: The base directory from which files and directories are served.
- `Interface`: The network interface to listen on (e.g., `*` for all interfaces).
- `Port`: The port number to listen on.
- `ShowNotification`: A boolean flag to show a notification on the directory listing page.

## Usage

To run the server, execute the program with the path to the configuration file as an argument. If no argument is provided, the server will use a default configuration file located in the user's application data directory.

- Windows: `%APPDATA%\MediaServer\config.json`,
- macOS: `~/Library/Application Support/MediaServer/config.json`,
- Linux: `~/.config/MediaServer/config.json`
