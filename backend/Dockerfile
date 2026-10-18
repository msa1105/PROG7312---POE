FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["SmartX.Api.csproj", "./"]
RUN dotnet restore "./SmartX.Api.csproj"
COPY . .
RUN dotnet build "SmartX.Api.csproj" -c Release -o /app/build
RUN dotnet publish "SmartX.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000
RUN mkdir -p /app/Uploads /app/keys
ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
