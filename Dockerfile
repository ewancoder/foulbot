FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS base
# ICU for Russian text comparisons, tzdata for the chat time zone.
RUN apk add --no-cache icu-data-full icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
USER app
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["Directory.Build.props", "Directory.Packages.props", ".editorconfig", "./"]
COPY ["src/PidorBot/PidorBot.csproj", "src/PidorBot/"]
RUN dotnet restore "src/PidorBot/PidorBot.csproj"
COPY src/ src/
RUN dotnet publish "src/PidorBot/PidorBot.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PidorBot.dll"]
