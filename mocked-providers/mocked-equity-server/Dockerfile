# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file
COPY MockedEquity.API/MockedEquity.API.csproj MockedEquity.API/
RUN dotnet restore "MockedEquity.API/MockedEquity.API.csproj"

# Copy source code
COPY MockedEquity.API/ MockedEquity.API/

# Build
WORKDIR /src/MockedEquity.API
RUN dotnet build "MockedEquity.API.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "MockedEquity.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5104
ENV ASPNETCORE_URLS=http://+:5104
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MockedEquity.API.dll"]
