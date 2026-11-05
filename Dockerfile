# ELEGIDO MANAGER - imagen para la nube (Vercel, Render, Railway, etc.)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ElegidoManager/ElegidoManager.csproj ElegidoManager/
RUN dotnet restore ElegidoManager/ElegidoManager.csproj
COPY ElegidoManager/ ElegidoManager/
RUN dotnet publish ElegidoManager/ElegidoManager.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
# Fuentes y fontconfig para generar los contratos PDF en Linux
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 fonts-dejavu-core tzdata \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish ./
ENV PORT=80 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    TZ=America/Mexico_City
ENTRYPOINT ["dotnet", "ElegidoManager.dll"]
