FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY translate.slnx .
COPY src/ src/
RUN dotnet restore src/Lopatnov.Translate.Grpc/Lopatnov.Translate.Grpc.csproj

RUN dotnet publish src/Lopatnov.Translate.Grpc/Lopatnov.Translate.Grpc.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app/src/Lopatnov.Translate.Grpc

COPY --from=build /app/publish .

RUN useradd --system --no-create-home --shell /sbin/nologin appuser \
    && chown -R appuser:appuser /app
USER appuser

ENV ASPNETCORE_HTTP_PORTS=5100
EXPOSE 5100

ENTRYPOINT ["dotnet", "Lopatnov.Translate.Grpc.dll"]
