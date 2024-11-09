FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Producer.Classic/Producer.Classic.csproj", "Producer.Classic/"]
COPY ["Common/Common.csproj", "Common/"]
RUN dotnet restore "Producer.Classic/Producer.Classic.csproj"
COPY . .
WORKDIR "/src/Producer.Classic"
RUN dotnet build "Producer.Classic.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Producer.Classic.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Producer.Classic.dll"]
