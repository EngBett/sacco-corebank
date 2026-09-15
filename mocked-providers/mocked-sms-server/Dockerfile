FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MockedSms.API/MockedSms.API.csproj MockedSms.API/
RUN dotnet restore "MockedSms.API/MockedSms.API.csproj"
COPY MockedSms.API/ MockedSms.API/
WORKDIR /src/MockedSms.API
RUN dotnet publish "MockedSms.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MockedSms.API.dll"]
