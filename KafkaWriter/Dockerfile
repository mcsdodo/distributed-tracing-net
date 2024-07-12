FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["KafkaWriter/KafkaWriter.csproj", "KafkaWriter/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "KafkaWriter/KafkaWriter.csproj"
COPY . .
WORKDIR "/src/KafkaWriter"
RUN dotnet build "KafkaWriter.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "KafkaWriter.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KafkaWriter.dll"]
