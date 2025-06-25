FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["DotNetNakamaServer/DotNetNakamaServer.csproj", "DotNetNakamaServer/"]
RUN dotnet restore "DotNetNakamaServer/DotNetNakamaServer.csproj"
COPY . .
WORKDIR "/src/DotNetNakamaServer"
RUN dotnet build "DotNetNakamaServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "DotNetNakamaServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DotNetNakamaServer.dll"]
