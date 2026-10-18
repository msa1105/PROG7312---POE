FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY ["SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj", "SmartX.DeviceSimulator/"]
RUN dotnet restore "SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj"
COPY . .
RUN dotnet publish "SmartX.DeviceSimulator/SmartX.DeviceSimulator.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV SMARTX_API_URL=http://backend:5000
ENTRYPOINT ["dotnet", "SmartX.DeviceSimulator.dll"]
