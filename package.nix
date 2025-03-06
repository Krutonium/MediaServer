{ lib, buildDotnetModule, dotnetCorePackages, ffmpeg, yt-dlp }:

buildDotnetModule rec {
  pname = "MediaServer";
  version = "1.0";

  src = ./.;

  projectFile = "./MediaServer.sln";
  dotnet-sdk = dotnetCorePackages.sdk_9_0;
  dotnet-runtime = dotnetCorePackages.sdk_9_0;
  dotnetFlags = [ "" ];
  executables = [ "MediaServer" ];
  runtimeDeps = [ "" ];
}
