# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/RagDemo.Api/RagDemo.Api.csproj src/RagDemo.Api/
RUN dotnet restore src/RagDemo.Api/RagDemo.Api.csproj
COPY src/ src/
RUN dotnet publish src/RagDemo.Api/RagDemo.Api.csproj -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Cloud Run sends traffic to $PORT (8080 by default).
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RagDemo.Api.dll"]
