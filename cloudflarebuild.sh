curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
./dotnet/dotnet --version

# Add dotnet to PATH so MSBuild targets (e.g. Workers.targets) can invoke the `dotnet` command
export PATH="$(pwd)/dotnet:$PATH"
export DOTNET_ROOT="$(pwd)/dotnet"

# Publish the BlazorWebApp and copy its static files into the worker's dist folder
# so worker.js can serve it as static files (see [assets] in wrangler.toml).
./dotnet/dotnet publish src/BlazorWebApp -c Release -o ./publish-blazor
mkdir -p src/WorkersDotNet/dist/wwwroot
cp -r ./publish-blazor/wwwroot/. src/WorkersDotNet/dist/wwwroot/

# Publish the Worker (produces src/WorkersDotNet/dist/worker.js)
./dotnet/dotnet publish src/WorkersDotNet -c Release
