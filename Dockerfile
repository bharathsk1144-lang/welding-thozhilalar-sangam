# Backend build context is the repository root. .dockerignore excludes local data and secrets.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY server/WeldingThozhilalarSangam.Api.csproj server/
RUN dotnet restore server/WeldingThozhilalarSangam.Api.csproj
COPY server/ server/
RUN dotnet publish server/WeldingThozhilalarSangam.Api.csproj -c Release --no-restore -o /output /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /output/ ./
# Runtime configuration is supplied by the host. Do not ship local admin defaults.
RUN rm -f /app/appsettings.json /app/appsettings.Development.json
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
# Provision the private mounted /var/lib/wts/uploads directory for this UID;
# never rely on container-layer storage. Do not bake credentials into the image.
ENTRYPOINT ["dotnet", "WeldingThozhilalarSangam.Api.dll"]
