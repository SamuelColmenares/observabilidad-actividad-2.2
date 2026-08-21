# Actividad 2.2: Laboratorio de Observabilidad con Microservicios en .NET 10 y Docker Compose

Este repositorio contiene una solución completa para un laboratorio local de observabilidad basada en **.NET 10 Minimal API**, integrada con **OpenTelemetry**, **PostgreSQL**, **Couchbase**, **Prometheus**, **Grafana Tempo** y **Grafana Dashboard**, orquestados completamente a través de **Docker Compose**.

---

## 📊 Resultados de Verificación y Estado de la Solución

- **Compilación .NET 10**: Éxito total sin advertencias ni errores (`Build succeeded. 0 Warning(s), 0 Error(s)`).
- **Servicios Implementados**:
  - `Passengers` Service (Minimal API en puerto `5001`, base de datos PostgreSQL `passengers_db`).
  - `Checkin` Service (Minimal API en puerto `5002`, base de datos Couchbase cluster `airline`, bucket `checkin_bucket`, scope `_default` y collection `_default`).
- **Couchbase Auto-Init**:
  - Clúster auto-inicializado con el nombre **`airline`**.
  - Credenciales de administrador: usuario `Administrator`, contraseña `password`.
  - Creación automática del bucket **`checkin_bucket`** (RAM 256MB) si no existe.
  - Utilización explícita del **scope por defecto (`_default`)** y **collection por defecto (`_default`)**.
- **Observabilidad e Interfaz Visual Integrada**:
  - **Grafana UI (`:3000`)**: Aprovisionamiento automático de datasources para Prometheus y Grafana Tempo. Visualización e inspección interactiva de gráficos de llamas (*flamegraphs*) y búsqueda de trazas distribuidas.
  - **Traces**: OpenTelemetry SDK + Exporter OTLP (`:4317`) + Propagación de contexto distribuido W3C (`traceparent`).
  - **Metrics**: Instrumentos nativos `System.Diagnostics.Metrics` exportados vía OTLP hacia Prometheus (`:8889` -> `:9090`).
  - **Logs**: Logging estructurado con `ILogger` enriquecido con `CorrelationId` e ID de traza.
- **Docker Compose**: Contenedores en la red `observability-net` con volúmenes de datos nombrados para persistencia.

---

## 🏗️ Arquitectura del Sistema

La arquitectura está compuesta por contenedores interconectados mediante una red privada de Docker (`observability-net`):

```text
[ Cliente / Curl ]
        │
        ├───> Passengers Service (HTTP :5001) ───> PostgreSQL (:5432)
        │            │
        │            └───> OpenTelemetry Collector (:4317 OTLP)
        │                         │
        └───> Checkin Service (HTTP :5002)                        ├───> Prometheus (:9090) [Metrics] ────┐
                     │            │                               └───> Grafana Tempo (:3200) [Traces]   │
                     ├───(HTTP)───┘                                              │                        │
                     └───> Couchbase Cluster "airline" (:8091, :11210)         └──────> Grafana UI (:3000) ◄┘
                               └── Bucket "checkin_bucket" (_default / _default)
```

---

## 🚀 Inicialización del Entorno con Docker Compose

### Requisitos
- Docker Engine y Docker Compose instalados.

### 1. Iniciar todos los servicios
Desde la raíz del repositorio (`actividad-2.2`), ejecuta:

```bash
docker-compose up -d --build
```

### 2. Verificar el estado de los contenedores
```bash
docker-compose ps
```

### 3. Enlaces de Acceso Local
| Servicio / Herramienta | URL / Puerto | Descripción / Credenciales |
| :--- | :--- | :--- |
| **Grafana UI** | `http://localhost:3000` | **Visualizador gráfico de Trazas y Métricas** (`admin` / `admin`) |
| **Passengers API** | `http://localhost:5001` | API de gestión de pasajeros |
| **Checkin API** | `http://localhost:5002` | API de procesamiento de check-in |
| **Prometheus** | `http://localhost:9090` | Panel de métricas Prometheus |
| **Grafana Tempo API** | `http://localhost:3200` | Backend API de trazas distribuidas |
| **Couchbase Console** | `http://localhost:8091` | Consola NoSQL (`Administrator` / `password`) |
| **OTLP Collector gRPC** | `localhost:4317` | Puerto de recepción de OpenTelemetry |
| **OTLP Collector Metrics** | `http://localhost:8889/metrics` | Exporter de métricas en formato Prometheus |
| **PostgreSQL** | `localhost:5432` | Servidor de base de datos relacional |

---

## 👁️ Cómo Visualizar las Trazas Registradas en Tempo

Existen **3 formas principales** para inspeccionar las trazas distribuidas capturadas por Grafana Tempo:

### Opción 1: Grafana UI (Método Gráfico Recomendado 🎨)

1. Abre en tu navegador `http://localhost:3000` e inicia sesión (`admin` / `admin`).
2. En el menú lateral izquierdo, selecciona **Explore** (icono de compás).
3. En el selector superior de origen de datos (*DataSource*), elige **Tempo**.
4. Haz clic en la pestaña **Search** y selecciona el servicio (ej. `passengers-service` o `checkin-service`).
5. Haz clic en **Run query**. Aparecerá el listado de trazas recibidas.
6. Selecciona cualquier Trace ID para abrir el diagrama interconectado (*Flamegraph*), mostrando la cascada de llamadas HTTP entre `Checkin` -> `Passengers` -> `PostgreSQL` / `Couchbase`.

### Opción 2: API HTTP de Tempo (`curl` / Navegador 🌐)

Puedes consultar trazas directamente utilizando la API REST de Tempo:

```bash
# Consultar el estado y salud de Tempo
curl -X GET "http://localhost:3200/ready"

# Buscar trazas recientes en Tempo
curl -X GET "http://localhost:3200/api/v2/search"

# Consultar los detalles de una traza por su Trace ID
curl -X GET "http://localhost:3200/api/traces/<TRACE_ID>"
```

### Opción 3: Logs del OpenTelemetry Collector (Consola 💻)

El archivo `otel-collector-config.yaml` está configurado con el exportador `debug` en modo `detailed`. Cada vez que un microservicio envía un span, los detalles se imprimen en los logs del colector:

```bash
docker-compose logs -f otel-collector
```

---

## 🧪 Pruebas de Funcionamiento y Observabilidad (Curl)

### 1. Crear un Pasajero (Passengers Service)
```bash
curl -X POST "http://localhost:5001/passengers" \
  -H "Content-Type: application/json" \
  -d '{
    "id": "PAS-1001",
    "firstName": "Alice",
    "lastName": "Smith",
    "email": "alice.smith@example.com",
    "passportNumber": "P98765432"
  }'
```

### 2. Consultar Pasajero con Retraso Simulado (Passengers Service)
```bash
curl -X GET "http://localhost:5001/passengers/PAS-1001?delay=1500"
```

### 3. Procesar Check-in con Validación HTTP Inter-Servicio (Checkin Service)
```bash
curl -X POST "http://localhost:5002/checkin" \
  -H "Content-Type: application/json" \
  -d '{
    "passengerId": "PAS-1001",
    "flightNumber": "AV302",
    "seatNumber": "14C",
    "baggageCount": 1
  }'
```

### 4. Simular Error en Check-in para Pruebas de Alertas
```bash
curl -X POST "http://localhost:5002/checkin?error=true" \
  -H "Content-Type: application/json" \
  -d '{
    "passengerId": "PAS-1001",
    "flightNumber": "AV302",
    "seatNumber": "14C",
    "baggageCount": 1
  }'
```

---

## 🧹 Detener y Limpiar el Entorno

Para detener todos los contenedores y remover la red personalizada:
```bash
docker-compose down
```

Para detener los contenedores y eliminar los volúmenes de datos persistentes:
```bash
docker-compose down -v
```
