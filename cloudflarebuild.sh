curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
./dotnet/dotnet --version

# Add dotnet to PATH so MSBuild targets (e.g. Workers.targets) can invoke the `dotnet` command
export PATH="$(pwd)/dotnet:$PATH"
export DOTNET_ROOT="$(pwd)/dotnet"

./dotnet/dotnet publish src -c Release
