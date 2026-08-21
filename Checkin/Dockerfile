# Multi-stage Dockerfile for Checkin service (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["Checkin.csproj", "./"]
RUN dotnet restore "Checkin.csproj"

# Copy source code and publish release build
COPY . .
RUN dotnet publish "Checkin.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Checkin.dll"]
