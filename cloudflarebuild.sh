set -e

# Use an existing dotnet on PATH if available, otherwise install one locally.
# dotnet-install.sh only supports real Linux/macOS, so on Windows (Git Bash/MINGW)
# we rely on a system-installed dotnet already being on PATH.
if ! command -v dotnet >/dev/null 2>&1 && [ ! -x ./dotnet/dotnet ]; then
  echo "dotnet not found; installing to ./dotnet ..."
  curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
  chmod +x dotnet-install.sh
  ./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
  export PATH="$(pwd)/dotnet:$PATH"
  export DOTNET_ROOT="$(pwd)/dotnet"
fi

dotnet --version

# Publish the BlazorWebApp and copy its static files into the worker's dist folder
# so worker.js can serve it as static files (see [assets] in wrangler.toml).
dotnet publish src/BlazorWebApp -c Release -o ./publish-blazor
mkdir -p src/WorkersDotNet/dist/wwwroot
cp -r ./publish-blazor/wwwroot/. src/WorkersDotNet/dist/wwwroot/

# Publish the Worker (produces src/WorkersDotNet/dist/worker.js)
dotnet publish src/WorkersDotNet -c Release
