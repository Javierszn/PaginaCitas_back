# ---- Etapa 1: build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiamos solo el csproj primero para aprovechar el cache de Docker
COPY RegistroCivilAPI.csproj ./
RUN dotnet restore "RegistroCivilAPI.csproj"

# Copiamos el resto del código y publicamos
COPY . .
RUN dotnet publish "RegistroCivilAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ---- Etapa 2: runtime (imagen final, más ligera) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Railway inyecta la variable PORT; le decimos a Kestrel que escuche ahí
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "RegistroCivilAPI.dll"]