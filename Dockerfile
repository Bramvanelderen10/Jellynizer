FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY src/Jellyniser/Jellyniser.csproj src/Jellyniser/
RUN dotnet restore src/Jellyniser/Jellyniser.csproj

# Copy everything else and build
COPY src/Jellyniser/ src/Jellyniser/
RUN dotnet publish src/Jellyniser/Jellyniser.csproj -c Release -o /app/publish

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Set timezone to your local timezone (change as needed)
ENV TZ=Europe/Amsterdam
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# Install gosu for privilege de-escalation so that moved files are owned by
# the host user (PUID/PGID) rather than root, preventing SMB lock-out.
RUN apt-get update \
    && apt-get install -y --no-install-recommends gosu \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 45263

ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "Jellyniser.dll"]
