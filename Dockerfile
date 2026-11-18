# ELEGIDO MANAGER - imagen para la nube (Vercel, Render, Railway, etc.)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ElegidoManager/ElegidoManager.csproj ElegidoManager/
RUN dotnet restore ElegidoManager/ElegidoManager.csproj -r linux-x64 -p:PublishReadyToRun=true
COPY ElegidoManager/ ElegidoManager/
# Calendario (FullCalendar) dentro de la app: la Agenda ya no depende de otro servidor.
# Si no hubiera internet al construir, la Agenda lo toma del CDN como antes.
RUN mkdir -p ElegidoManager/wwwroot/lib/fullcalendar \
 && ( curl -fsSL --retry 3 -o ElegidoManager/wwwroot/lib/fullcalendar/index.global.min.js https://cdn.jsdelivr.net/npm/fullcalendar@6.1.15/index.global.min.js \
   && curl -fsSL --retry 3 -o ElegidoManager/wwwroot/lib/fullcalendar/es.global.min.js https://cdn.jsdelivr.net/npm/@fullcalendar/core@6.1.15/locales/es.global.min.js \
   || ( echo "Sin internet: la Agenda usará el CDN" && rm -f ElegidoManager/wwwroot/lib/fullcalendar/*.js ) )
# ReadyToRun: el código va precompilado, así la app arranca más rápido cuando Vercel la "despierta"
RUN dotnet publish ElegidoManager/ElegidoManager.csproj -c Release -r linux-x64 --self-contained false --no-restore \
    -p:PublishReadyToRun=true -o /app/publish /p:UseAppHost=false

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
