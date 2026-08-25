# syntax=docker/dockerfile:1

FROM node:24-bookworm-slim AS web-build
WORKDIR /src

COPY package.json package-lock.json ./
COPY apps/learner-web/package.json apps/learner-web/package.json
RUN npm ci

COPY apps/learner-web apps/learner-web

ARG VITE_AUTH_MODE=entra
ARG VITE_API_BASE_URL=
ARG VITE_ENTRA_CLIENT_ID
ARG VITE_ENTRA_TENANT_ID
ARG VITE_ENTRA_API_SCOPE
ARG VITE_ENTRA_REDIRECT_URI
ENV VITE_AUTH_MODE=$VITE_AUTH_MODE \
    VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_ENTRA_CLIENT_ID=$VITE_ENTRA_CLIENT_ID \
    VITE_ENTRA_TENANT_ID=$VITE_ENTRA_TENANT_ID \
    VITE_ENTRA_API_SCOPE=$VITE_ENTRA_API_SCOPE \
    VITE_ENTRA_REDIRECT_URI=$VITE_ENTRA_REDIRECT_URI
RUN npm run build --workspace @fde-tutor/learner-web

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src

COPY Directory.Build.props global.json FdeTutor.sln ./
COPY apps/platform-api/FdeTutor.Api.csproj apps/platform-api/
COPY packages/learning-contract/dotnet/FdeTutor.Contracts.csproj packages/learning-contract/dotnet/
COPY packages/platform-domain/FdeTutor.Domain.csproj packages/platform-domain/
COPY packages/platform-persistence/FdeTutor.Persistence.csproj packages/platform-persistence/
RUN dotnet restore apps/platform-api/FdeTutor.Api.csproj

COPY apps/platform-api apps/platform-api
COPY packages packages
RUN dotnet publish apps/platform-api/FdeTutor.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app

COPY --from=api-build /app/publish ./
COPY --from=web-build /src/apps/learner-web/dist ./wwwroot
COPY content-package ./content-package
COPY infra/db/migrations ./migrations

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ContentPackage__Root=/app/content-package \
    Database__MigrationsRoot=/app/migrations
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "FdeTutor.Api.dll"]
