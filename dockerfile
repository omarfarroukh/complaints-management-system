# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj files separately to optimize layer caching
COPY ["src/CMS.Api/CMS.Api.csproj", "src/CMS.Api/"]
COPY ["src/CMS.Infrastructure/CMS.Infrastructure.csproj", "src/CMS.Infrastructure/"]
COPY ["src/CMS.Application/CMS.Application.csproj", "src/CMS.Application/"]
COPY ["src/CMS.Domain/CMS.Domain.csproj", "src/CMS.Domain/"]

# Restore packages
RUN dotnet restore "src/CMS.Api/CMS.Api.csproj"

# Copy the rest of the code
COPY . .
WORKDIR "/src/src/CMS.Api"
RUN dotnet build "CMS.Api.csproj" -c Release -o /app/build

# Publish Stage
FROM build AS publish
RUN dotnet publish "CMS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CMS.Api.dll"]