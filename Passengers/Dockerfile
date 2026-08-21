# Multi-stage Dockerfile for Passengers service (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["Passengers.csproj", "./"]
RUN dotnet restore "Passengers.csproj"

# Copy source code and publish release build
COPY . .
RUN dotnet publish "Passengers.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Passengers.dll"]
