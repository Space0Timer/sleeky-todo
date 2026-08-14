# syntax=docker/dockerfile:1

# The client build and the API build are independent stages, so a change to one
# reuses the other's cached layers.
FROM node:24.19.0-alpine AS web
WORKDIR /web
COPY src/sleeky-todo-web/package.json \
     src/sleeky-todo-web/yarn.lock \
     src/sleeky-todo-web/.yarnrc.yml ./
RUN corepack enable && yarn install --immutable
COPY src/sleeky-todo-web/ ./
RUN yarn build

# Pinned to the exact SDK in global.json. The floating 10.0 tag carries a later
# feature band, which the file's latestPatch roll-forward refuses, so the two
# have to be bumped together.
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS api
WORKDIR /src
# Project files are restored before any source is copied, so editing a .cs file
# does not invalidate the restore layer.
COPY global.json Directory.Build.props Directory.Packages.props stylecop.json ./
COPY src/Sleeky.Todo.Domain/Sleeky.Todo.Domain.csproj src/Sleeky.Todo.Domain/
COPY src/Sleeky.Todo.Application/Sleeky.Todo.Application.csproj src/Sleeky.Todo.Application/
COPY src/Sleeky.Todo.Infrastructure/Sleeky.Todo.Infrastructure.csproj src/Sleeky.Todo.Infrastructure/
COPY src/Sleeky.Todo.Assistant/Sleeky.Todo.Assistant.csproj src/Sleeky.Todo.Assistant/
COPY src/Sleeky.Todo.Api/Sleeky.Todo.Api.csproj src/Sleeky.Todo.Api/
RUN dotnet restore src/Sleeky.Todo.Api/Sleeky.Todo.Api.csproj

# The analyzer severity map is a compile input like the sources below it, so it
# joins them after the restore layer. Without it the image compiles under
# stricter rules than CI, and warnings-as-errors turns that drift fatal.
COPY .editorconfig ./
COPY src/Sleeky.Todo.Domain/ src/Sleeky.Todo.Domain/
COPY src/Sleeky.Todo.Application/ src/Sleeky.Todo.Application/
COPY src/Sleeky.Todo.Infrastructure/ src/Sleeky.Todo.Infrastructure/
COPY src/Sleeky.Todo.Assistant/ src/Sleeky.Todo.Assistant/
COPY src/Sleeky.Todo.Api/ src/Sleeky.Todo.Api/
RUN dotnet publish src/Sleeky.Todo.Api/Sleeky.Todo.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=api /app/publish ./

# The client ships in the same image so that it can later be served from the
# same origin as the API, which gives the session cookie a single origin to
# belong to. The host still needs static-file and SPA fallback configuration
# before these files are reachable; until then they are inert.
COPY --from=web /web/dist ./wwwroot

# No HTTPS port is configured because TLS is terminated ahead of this
# container; the redirect middleware becomes a no-op when it cannot resolve a
# target port.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The key ring encrypts session cookies and every user's stored provider key, so
# losing it signs everyone out and leaves saved keys unreadable. The directory
# is created here, owned by the user the app runs as, because a named volume
# takes its ownership from the image directory it covers: mounted over a path
# that does not exist, it arrives owned by root and the app cannot write it.
#
# Without a volume the keys still live on the container's writable layer and
# still vanish with it. Creating the directory makes persisting them a mount
# rather than a mount plus a setting.
RUN mkdir /keys && chown $APP_UID /keys
ENV DataProtection__KeyRingPath=/keys

USER $APP_UID
ENTRYPOINT ["dotnet", "Sleeky.Todo.Api.dll"]
