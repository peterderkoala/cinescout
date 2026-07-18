# Build context is the repo root (see docker-compose.yml's `build.context: ..`) so this stage
# can COPY from src/ — a Dockerfile can't reach outside its build context.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first and restore before the rest of the source, so the (slow) restore
# layer stays cached across source-only changes.
COPY src/cinescout.slnx src/cinescout.slnx
COPY src/cinescout.web/cinescout.web.csproj src/cinescout.web/
COPY src/cinescout.web.Client/cinescout.web.Client.csproj src/cinescout.web.Client/
COPY src/cinescout.core/cinescout.core.csproj src/cinescout.core/
COPY src/cinescout.model/cinescout.model.csproj src/cinescout.model/
COPY src/cinescout.persistence/cinescout.persistence.csproj src/cinescout.persistence/
RUN dotnet restore src/cinescout.web/cinescout.web.csproj

COPY src/cinescout.web/ src/cinescout.web/
COPY src/cinescout.web.Client/ src/cinescout.web.Client/
COPY src/cinescout.core/ src/cinescout.core/
COPY src/cinescout.model/ src/cinescout.model/
COPY src/cinescout.persistence/ src/cinescout.persistence/
RUN dotnet publish src/cinescout.web/cinescout.web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
# Pre-create the DataProtection key directory owned by the app user before the compose-level
# volume mount happens — Docker copies a named volume's initial content (incl. ownership) from
# whatever already exists at the mount point in the image the first time the volume is used, so
# this is what makes that volume writable by a non-root container instead of root-owned.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown $APP_UID:$APP_UID /home/app/.aspnet/DataProtection-Keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "cinescout.web.dll"]
