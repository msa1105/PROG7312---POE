# Smart-X IoT Mesh Ecosystem (PoE Part 1)

This project implements the ingestion gateway and management dashboard for the Smart-X IoT mesh ecosystem.

## Technology Stack
- **Backend:** .NET 10 ASP.NET Core Minimal APIs
- **Frontend:** React 19, TypeScript, Vite, Tailwind CSS (v4), Lucide Icons
- **Containerization:** Docker & Docker Compose

## Features Implemented
- **Sensor Telemetry Ingestion:** Generic `TelemetryPacket<T>` without boxing/unboxing overhead.
- **Operator Overloading:** `PowerMetric` struct overriding arithmetic operators (`+`, `-`, `>`, `<`).
- **Jagged Arrays:** High-throughput telemetry batching via `T[][]`.
- **Recursion:** Deep tree validation of `DeploymentNode` topologies.
- **Progressive Disclosure UI:** Advanced visual diagnostics with a sliding drawer for deep packet inspection.
- **Anomaly Detection:** Rolling Z-score anomaly detection ($|Z| > 3.0$) and automated lease-time disconnect timeouts.
- **Media Upload:** Multipart API upload handler for hardware logs and firmware binaries.

## How to Run Locally

### Prerequisites
- Docker Engine & Docker Compose
- Node 22 (if running frontend without docker)
- .NET 10 SDK (if running backend without docker)

### Running with Docker Compose (Recommended)
This requires only Docker to be installed.

1. Navigate to the root directory of the project.
2. Build and spin up both containers:
```bash
docker compose up --build
```
3. Access the dashboard at [http://localhost:3000](http://localhost:3000)
4. The Minimal API is available at [http://localhost:5000](http://localhost:5000)

### Testing the App
1. Register a new sensor using the `Register Node` form on the left.
2. Use the **Telemetry Simulator** to start injecting normal telemetry (value around 50).
3. Wait for the 2-second auto-refresh to show your metrics on the grid.
4. Click **Inject Data Spike** to simulate a random massive telemetry spike. The card will highlight red.
5. Stop simulation and wait ~10 seconds to observe the node drop into **OFFLINE** degraded state.
6. Click the node card to open the **Anomaly Drawer** to view raw payloads and upload attachments.
7. Paste JSON hierarchy structures into the **Topology Validator** to test the recursive validator.

## License
MIT
