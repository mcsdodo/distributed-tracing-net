FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["KafkaReader.Worker/KafkaReader.Worker.csproj", "KafkaReader.Worker/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "KafkaReader.Worker/KafkaReader.Worker.csproj"
COPY . .
WORKDIR "/src/KafkaReader.Worker"
RUN dotnet build "KafkaReader.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "KafkaReader.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KafkaReader.Worker.dll"]
