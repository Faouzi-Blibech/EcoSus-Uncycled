# Build from the repository root so that content/ is in the context:
#   docker build -f server/Dockerfile -t uncycled-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/Sim/ server/Sim/
COPY server/Uncycled.Server/ server/Uncycled.Server/
COPY bots/Uncycled.Bots/ bots/Uncycled.Bots/
COPY bots/Uncycled.Bots.Neural/ bots/Uncycled.Bots.Neural/
# linux-x64 only: keeps just that platform's native ONNX Runtime library in the image
RUN dotnet publish server/Uncycled.Server/Uncycled.Server.csproj -c Release -r linux-x64 --self-contained false -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
COPY content/ ./content/
COPY bots/bots.json ./bots/bots.json
COPY bots/models/ ./bots/models/
# Bots: bots.json says "scripted". To play networks, add models/ and set UNCYCLED_BOTS_BRAIN=neural
# (see bots/README.md); no rebuild of bots.json needed.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    UNCYCLED_CONTENT_DIR=/app/content \
    UNCYCLED_BOTS_DIR=/app/bots
EXPOSE 8080
ENTRYPOINT ["dotnet", "Uncycled.Server.dll"]
