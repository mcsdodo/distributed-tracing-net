FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["RedisStreamReader.Worker/RedisStreamReader.Worker.csproj", "RedisStreamReader.Worker/"]
COPY ["Common/Common.csproj", "Common/"]
COPY ["Common.Redis/Common.Redis.csproj", "Common.Redis/"]
RUN dotnet restore "RedisStreamReader.Worker/RedisStreamReader.Worker.csproj"
COPY . .
WORKDIR "/src/RedisStreamReader.Worker"
RUN dotnet build "RedisStreamReader.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "RedisStreamReader.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RedisStreamReader.Worker.dll"]
