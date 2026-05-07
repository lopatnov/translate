FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY translate.slnx .
COPY src/ src/
RUN dotnet restore src/Lopatnov.Translate.Grpc/Lopatnov.Translate.Grpc.csproj && \
    dotnet publish src/Lopatnov.Translate.Grpc/Lopatnov.Translate.Grpc.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app/src/Lopatnov.Translate.Grpc

RUN apt-get update && apt-get install -y --no-install-recommends \
    libgomp1=14.2.0-4ubuntu2~24.04.1 \
    git=1:2.43.0-1ubuntu7.3 \
    build-essential=12.10ubuntu1 \
    cmake=3.28.3-1build7 \
    && rm -rf /var/lib/apt/lists/* && \
    git clone https://github.com/ggerganov/whisper.cpp.git /tmp/whisper.cpp && \
    mkdir -p /tmp/whisper.cpp/build && \
    cmake -B /tmp/whisper.cpp/build -S /tmp/whisper.cpp && \
    cmake --build /tmp/whisper.cpp/build --config Release && \
    cmake --install /tmp/whisper.cpp/build && \
    rm -rf /tmp/whisper.cpp && \
    useradd --system --no-create-home --shell /sbin/nologin appuser && \
    chown -R appuser:appuser /app

COPY --from=build /app/publish .

USER appuser

ENV ASPNETCORE_HTTP_PORTS=5100
EXPOSE 5100

ENTRYPOINT ["dotnet", "Lopatnov.Translate.Grpc.dll"]
