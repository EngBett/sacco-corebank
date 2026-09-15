FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MockedNcba.API/MockedNcba.API.csproj MockedNcba.API/
RUN dotnet restore "MockedNcba.API/MockedNcba.API.csproj"
COPY MockedNcba.API/ MockedNcba.API/
WORKDIR /src/MockedNcba.API
RUN dotnet publish "MockedNcba.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5103
EXPOSE 5103
ENTRYPOINT ["dotnet", "MockedNcba.API.dll"]
