FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/NitroDrp.Api/NitroDrp.Api.csproj src/NitroDrp.Api/
RUN dotnet restore src/NitroDrp.Api/NitroDrp.Api.csproj
COPY src/ src/
RUN dotnet publish src/NitroDrp.Api/NitroDrp.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "NitroDrp.Api.dll"]
