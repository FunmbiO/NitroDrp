FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# --locked-mode: build with exactly the package versions in packages.lock.json, or fail.
COPY src/NitroDrp.Api/NitroDrp.Api.csproj src/NitroDrp.Api/packages.lock.json src/NitroDrp.Api/
RUN dotnet restore src/NitroDrp.Api/NitroDrp.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/NitroDrp.Api/NitroDrp.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "NitroDrp.Api.dll"]
