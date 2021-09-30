FROM mcr.microsoft.com/dotnet/sdk:5.0 as build
WORKDIR /build
RUN git clone --depth=1 -b separation https://github.com/Coflnet/HypixelSkyblock.git dev
WORKDIR /build/sky
COPY SkySubscriptions.csproj SkySubscriptions.csproj
RUN dotnet restore
COPY . .
RUN dotnet publish -c release

FROM mcr.microsoft.com/dotnet/aspnet:5.0
WORKDIR /app

COPY --from=build /build/sky/bin/release/net5.0/publish/ .
RUN mkdir -p ah/files

ENTRYPOINT ["dotnet", "SkySubscriptions.dll"]

VOLUME /data
