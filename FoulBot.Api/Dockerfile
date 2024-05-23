FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Build.props"]
COPY ["FoulBot.Domain/FoulBot.Domain.csproj", "FoulBot.Domain/"]
COPY ["FoulBot.Api/FoulBot.Api.csproj", "FoulBot.Api/"]
RUN dotnet restore "./FoulBot.Api/FoulBot.Api.csproj"
COPY . .
WORKDIR "/src/FoulBot.Api"
RUN dotnet build "./FoulBot.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FoulBot.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FoulBot.Api.dll"]
