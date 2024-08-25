FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS base
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["FoulBot.App/FoulBot.App.csproj", "FoulBot.App/"]
COPY ["FoulBot.Domain/FoulBot.Domain.csproj", "FoulBot.Domain/"]
COPY ["FoulBot.Infrastructure/FoulBot.Infrastructure.csproj", "FoulBot.Infrastructure/"]
RUN dotnet restore "./FoulBot.App/FoulBot.App.csproj"
COPY . .
WORKDIR "/src/FoulBot.App"
RUN dotnet build "./FoulBot.App.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FoulBot.App.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoulBot.App.dll"]
