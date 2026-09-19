# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file
COPY ["MockedMpesa.API/MockedMpesa.API.csproj", "MockedMpesa.API/"]

# Restore dependencies
RUN dotnet restore "MockedMpesa.API/MockedMpesa.API.csproj"

# Copy everything else
COPY . .

# Build and publish
WORKDIR "/src/MockedMpesa.API"
RUN dotnet publish "MockedMpesa.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for healthchecks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

EXPOSE 5100

ENTRYPOINT ["dotnet", "MockedMpesa.API.dll"]
