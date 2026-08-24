# Actividad 2.2: Laboratorio de Observabilidad con Microservicios en .NET 10, GKE y Docker Compose

Este repositorio contiene una solución completa de observabilidad basada en **.NET 10 Minimal API**, instrumentada con **OpenTelemetry SDK** (trazas, métricas y logs estructurados), con un **OTel Collector** desplegado en **GKE Autopilot** que exporta hacia **Jaeger** (trazas), **Prometheus** (métricas) y **Cloud Logging** (logs). El entorno local usa **Docker Compose**; en la nube, **PostgreSQL/Couchbase** viven en una VM de GCE, el plano de observabilidad vive en GKE, y `Passengers`/`Checkin` se despliegan en **Cloud Run**.

> **Alcance:** esta actividad cubre únicamente **GCP** (Cloud Run + GKE Autopilot + GCE). No se implementa la variante AWS (ECS Fargate + X-Ray) mencionada como alternativa en el enunciado del laboratorio — ver la sección **🚫 Alcance y Exclusiones** al final de este documento.

---

## 📊 Resultados de Verificación y Estado de la Solución

- **Compilación .NET 10**: Éxito total sin advertencias ni errores (`Build succeeded. 0 Warning(s), 0 Error(s)`).
- **Servicios Implementados**:
  - `Passengers` Service (Minimal API en puerto `5001`, base de datos PostgreSQL `passengers_db`).
  - `Checkin` Service (Minimal API en puerto `5002`, base de datos Couchbase cluster `airline`, bucket `checkin_bucket`, scope `_default` y collection `_default`).
- **Couchbase Auto-Init**:
  - Clúster auto-inicializado con el nombre **`airline`**.
  - Credenciales de administrador: leídas de variables de entorno (`CB_ADMIN_USER`, `CB_ADMIN_PASSWORD`), con fallback a `Administrator` / `password` para desarrollo local.
  - Creación automática del bucket **`checkin_bucket`** (RAM 256MB) si no existe.
  - Utilización explícita del **scope por defecto (`_default`)** y **collection por defecto (`_default`)**.
- **Observabilidad e Interfaz Visual Integrada**:
  - **Grafana UI (`:3000`)**: Aprovisionamiento automático de datasources para Prometheus, Jaeger y Google Cloud Logging.
  - **Traces**: OpenTelemetry SDK + Exporter OTLP (`:4317`) + Propagación de contexto distribuido W3C (`traceparent`), custom spans de negocio (`GetPassengerById`, `CreatePassenger`, `ProcessCheckin`, `ValidatePassengerHttp`, `PersistCheckinCouchbase`).
  - **Metrics**: Instrumentos nativos `System.Diagnostics.Metrics` + `OpenTelemetry.Instrumentation.Runtime` (CPU/GC/memoria del proceso .NET) exportados vía OTLP hacia Prometheus (`:8889` y métricas internas del Collector en `:8888`).
  - **Logs**: Logging estructurado en **JSON** (`OtelJsonConsoleFormatter`) enriquecido con `trace_id`/`span_id` del `Activity` activo, para correlación logs ↔ trazas.
- **Docker Compose**: Contenedores en la red `observability-net` con volúmenes de datos nombrados para persistencia.

---

## 🏗️ Arquitectura del Sistema

### Entorno local (Docker Compose)

```text
[ Cliente / Curl ]
        │
        ├───> Passengers Service (HTTP :5001) ───> PostgreSQL (:5432)
        │            │
        │            └───> OpenTelemetry Collector (:4317 OTLP, :8888 métricas internas)
        │                         │
        └───> Checkin Service (HTTP :5002)                        ├───> Prometheus (:9090) [Metrics] ────┐
                     │            │                               └───> Jaeger (:16686) [Traces]          │
                     ├───(HTTP)───┘                                              │                        │
                     └───> Couchbase Cluster "airline" (:8091, :11210)         └──────> Grafana UI (:3000) ◄┘
                               └── Bucket "checkin_bucket" (_default / _default)
```

### Entorno en la nube (GCP)

```text
┌─────────────────────────────┐        ┌──────────────────────────────────────────────┐
│  Cloud Run                  │        │  GKE Autopilot — namespace "observability"    │
│  ├── passengers-service     │  OTLP  │  ├── otel-collector (memory_limiter→resource→ │
│  └── checkin-service ───────┼───────>│  │    batch) ── LoadBalancer :4317/:4318      │
└──────────────┬──────────────┘        │  ├── jaeger (trazas)     ── LoadBalancer :16686│
               │ SQL / Couchbase       │  ├── prometheus (métricas)── ClusterIP :9090  │
               ▼                       │  └── grafana (dashboards + Explore)           │
┌─────────────────────────────┐        │         ├── datasource Prometheus             │
│  VM de GCE (observability-vm)│        │         ├── datasource Jaeger                 │
│  ├── PostgreSQL              │        │         └── datasource Google Cloud Logging   │
│  └── Couchbase "airline"     │        │             (Workload Identity, sin llaves)   │
└─────────────────────────────┘        └──────────────────────┬─────────────────────────┘
                                                                │ logs (googlecloud exporter)
                                                                ▼
                                                        Cloud Logging (GCP)
```

---

## 🚀 Inicialización del Entorno con Docker Compose

### Requisitos
- Docker Engine y Docker Compose instalados.

### 🧪 Ejecutar Pruebas Unitarias
Ambos microservicios incluyen proyectos de pruebas unitarias desarrollados con **xUnit**, **Moq** y **EF Core In-Memory**:

```bash
# Ejecutar pruebas unitarias de Passengers.Tests
dotnet test Passengers.Tests/Passengers.Tests.csproj

# Ejecutar pruebas unitarias de Checkin.Tests
dotnet test Checkin.Tests/Checkin.Tests.csproj

# Ejecutar todas las pruebas unitarias de la solución
dotnet test
```

> Los valores por defecto están embebidos en `docker-compose.yml`. No se requiere ningún archivo `.env` para desarrollo local.  
> Si quieres usar credenciales propias, copia `.env.example` a `.env` y modifica los valores.

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
| **Jaeger UI** | `http://localhost:16686` | UI de trazas distribuidas |
| **Couchbase Console** | `http://localhost:8091` | Consola NoSQL (`Administrator` / `password`) |
| **OTLP Collector gRPC** | `localhost:4317` | Puerto de recepción de OpenTelemetry |
| **OTLP Collector Metrics** | `http://localhost:8889/metrics` | Exporter de métricas en formato Prometheus (apps) |
| **OTLP Collector Internal Metrics** | `http://localhost:8888/metrics` | Métricas internas del propio Collector (CPU, spans rechazados, etc.) |
| **PostgreSQL** | `localhost:5432` | Servidor de base de datos relacional |

---

## 🤖 CI/CD con GitHub Actions

### Arquitectura de los Workflows

```text
┌──────────────────────────────────────────────────────────────────────────────────┐
│  Cambios en init-couchbase.sh o docker-compose.yml                               │
│                │                                                                  │
│                ▼                                                                  │
│  ┌─────────────────────────────┐                                                 │
│  │  prerequisites.yml          │  ──► GCE VM (e2-medium)                         │
│  │  Deploy Databases           │      PostgreSQL, Couchbase                      │
│  │  (workflow_dispatch / push) │                                                 │
│  └─────────────────────────────┘                                                 │
│                                                                                  │
│  Cambios en k8s/gcp/**                                                          │
│                │                                                                  │
│                ▼                                                                  │
│  ┌─────────────────────────────┐                                                 │
│  │  observability-gke.yml      │  ──► GKE Autopilot (namespace observability)   │
│  │  Deploy Observability Stack │      OTel Collector, Jaeger, Prometheus,        │
│  │  (workflow_dispatch / push) │      Grafana                                   │
│  └─────────────────────────────┘                                                 │
│                                                                                  │
│  Cambios en Passengers/**          Cambios en Checkin/**                         │
│  o Passengers.Tests/**             o Checkin.Tests/**                            │
│       │                                    │                                     │
│       ▼                                    ▼                                     │
│  ┌──────────────────┐           ┌──────────────────┐                             │
│  │ passengers.yml   │           │ checkin.yml      │                             │
│  │ 1. Build & Test  │           │ 1. Build & Test  │                             │
│  │ 2. Build & Push  │──────────►│ 2. Build & Push  │──► Artifact Registry        │
│  │ 3. Deploy to     │           │ 3. Deploy to     │──► Cloud Run                │
│  │    Cloud Run     │           │    Cloud Run     │    (PG/Couchbase → VM;      │
│  │    (OTLP → GKE)  │           │    (OTLP → GKE)  │     OTLP → Collector GKE)   │
│  └──────────────────┘           └──────────────────┘                             │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### Descripción de los Workflows

| Archivo | Trigger | Jobs Incluidos | Descripción |
| :--- | :--- | :--- | :--- |
| `prerequisites.yml` | push a `main` en `init-couchbase.sh`, `docker-compose.yml`; o manual (`workflow_dispatch`) | `deploy-infrastructure` | Despliega/actualiza **solo PostgreSQL y Couchbase** (+ init) en una VM de GCE vía SSH |
| `observability-gke.yml` | push a `main` en `k8s/gcp/**`; o manual (`workflow_dispatch`) | `deploy-observability` | Crea (si no existe) un clúster **GKE Autopilot**, aplica los manifiestos de `k8s/gcp/` y despliega OTel Collector, Jaeger, Prometheus y Grafana |
| `passengers.yml` | push / PR a `main` en `Passengers/**` o `Passengers.Tests/**`; o manual (`workflow_dispatch`) | `build-and-test` (CI)<br>`build-and-push` (CD)<br>`deploy-cloud-run` (CD) | Compila y corre UTs del servicio Passengers. Si pasa CI y es `main`, compila Docker, sube a Artifact Registry y despliega a Cloud Run |
| `checkin.yml` | push / PR a `main` en `Checkin/**` o `Checkin.Tests/**`; o manual (`workflow_dispatch`) | `build-and-test` (CI)<br>`build-and-push` (CD)<br>`deploy-cloud-run` (CD) | Compila y corre UTs del servicio Checkin. Si pasa CI y es `main`, compila Docker, sube a Artifact Registry y despliega a Cloud Run |

### Orden de Ejecución Obligatorio (Primera Vez)

```
1. prerequisites.yml        ← SIEMPRE primero (al menos una vez) — Postgres/Couchbase en la VM
2. observability-gke.yml    ← Segundo — crea el clúster GKE y el plano de observabilidad
3. passengers.yml           ← Tercero (Checkin depende de la URL de Passengers)
4. checkin.yml              ← Cuarto
```

> **Primera vez:** Después de que `observability-gke.yml` despliegue con éxito, copia la IP externa de `otel-collector` mostrada en el Step Summary y agrégala como secret `OTEL_COLLECTOR_ENDPOINT` (formato `http://<IP>:4317`). Después de que `passengers.yml` despliegue con éxito, copia la URL del servicio Cloud Run mostrada en el Step Summary y agrégala como secret `PASSENGERS_SERVICE_URL`. Ambos pasos son necesarios solo una vez.

---

### 🔑 GitHub Secrets Requeridos

Agrega los siguientes secrets en: **Settings → Secrets and variables → Actions → New repository secret**

#### Autenticación GCP
| Secret | Ejemplo / Descripción |
| :--- | :--- |
| `GCP_PROJECT_ID` | `mi-proyecto-123456` |
| `GCP_WIF_PROVIDER` | `projects/123456789/locations/global/workloadIdentityPools/github-pool/providers/github-provider` |
| `GCP_SERVICE_ACCOUNT` | `github-actions-sa@mi-proyecto-123456.iam.gserviceaccount.com` |

#### VM de GCE (bases de datos)
| Secret | Ejemplo / Descripción |
| :--- | :--- |
| `GCE_VM_NAME` | `observability-vm` |
| `GCE_VM_ZONE` | `us-central1-a` |
| `GCE_VM_EXTERNAL_IP` | IP pública estática de la VM (ej. `34.68.100.200`) |

#### GKE Autopilot (plano de observabilidad) — nuevos
| Secret | Ejemplo / Descripción |
| :--- | :--- |
| `GKE_CLUSTER_NAME` | `observability-gke` |
| `GKE_CLUSTER_REGION` | `us-central1` |
| `OTEL_COLLECTOR_ENDPOINT` | `http://<IP-externa-otel-collector>:4317`. Se obtiene tras el primer deploy de `observability-gke.yml` (ver Step Summary) |

#### Bases de Datos
| Secret | Valor por defecto local | Descripción |
| :--- | :--- | :--- |
| `CB_ADMIN_USER` | `Administrator` | Usuario admin de Couchbase |
| `CB_ADMIN_PASSWORD` | `password` | Contraseña admin de Couchbase |
| `POSTGRES_USER` | `postgres` | Usuario de PostgreSQL |
| `POSTGRES_PASSWORD` | `postgres` | Contraseña de PostgreSQL |
| `POSTGRES_DB` | `passengers_db` | Nombre de la base de datos |
| `GF_ADMIN_PASSWORD` | `admin` | Contraseña admin de Grafana (ahora usada por `observability-gke.yml` para crear el Secret `grafana-admin-credentials` en GKE) |

#### Inter-Servicio
| Secret | Descripción |
| :--- | :--- |
| `PASSENGERS_SERVICE_URL` | URL de Cloud Run de Passengers (ej. `https://passengers-service-xxx-uc.a.run.app`). Se obtiene tras el primer deploy de Passengers. |

---

### 🔐 Variables de Entorno — Credenciales de BD y Override en Cloud Run

#### Cómo funciona el flujo de credenciales

```
Desarrollo local           GitHub Actions (CI/CD)
──────────────────         ─────────────────────────────────────
docker-compose.yml         GitHub Secrets
  CB_ADMIN_USER=           → secrets.CB_ADMIN_USER
    ${CB_ADMIN_USER         → escrito en .env en la VM
      :-Administrator}      → inyectado en Cloud Run --set-env-vars
  (fallback seguro)         (valor real de producción)
```

- **Local**: `docker-compose.yml` usa `${VARIABLE:-default}`. Si no hay `.env`, usa los valores por defecto. El sistema funciona con `docker compose up` sin configuración adicional.
- **CI/CD en GCE VM**: El workflow escribe un archivo `.env` con los valores de los secrets antes de correr `docker compose`. Los volúmenes nombrados (`couchbase_data`, `postgres_data`) persisten los datos entre actualizaciones.
- **Cloud Run (Checkin/Passengers)**: Los env vars de `appsettings.json` son **sobreescritos** por `--set-env-vars` en el deploy de Cloud Run. Esto permite que la aplicación apunte a la VM de GCE en producción sin modificar el código.

#### Jerarquía de configuración en .NET (orden de mayor a menor precedencia)
```
1. Variables de entorno (--set-env-vars en Cloud Run)  ← Mayor precedencia
2. appsettings.{Environment}.json
3. appsettings.json                                     ← Menor precedencia
```

---

### 🧪 Bandera `OBSERVABILITY_ENABLED` — Benchmark de Overhead (Fase 4)

Ambos servicios (`Passengers`, `Checkin`) exponen una variable de entorno para **apagar por completo** el pipeline de OpenTelemetry en runtime, sin necesidad de recompilar ni redeployar una imagen distinta. Esto permite comparar el mismo binario **con instrumentación** vs **sin instrumentación** durante el benchmark de overhead (k6/locust) de la Fase 4.

| Variable | Valores | Efecto |
|---|---|---|
| `OBSERVABILITY_ENABLED` | `true` (default) / `false` | En `false`, **no se registra** ningún componente de OpenTelemetry (tracing, métricas, logging exporter, instrumentadores de AspNetCore/HttpClient/Npgsql/Runtime). Solo queda un logger de consola simple. Es el escenario **baseline** del benchmark. |
| `OTEL_SDK_DISABLED` | `true` / `false` (default) | Variable **estándar** de la spec de OpenTelemetry, respetada automáticamente por el SDK. En `true`, el SDK sigue registrado (instrumentadores activos) pero deja de exportar datos (modo no-op). Útil para aislar solo el overhead de **red/exportación**, distinto del overhead total de instrumentación. |

#### Cómo usarla

**Local (Docker Compose):**
```bash
# Sin instrumentación (baseline)
OBSERVABILITY_ENABLED=false docker compose up -d passengers checkin

# Con instrumentación (default, no requiere la variable)
docker compose up -d passengers checkin
```

**Cloud Run (alternar sin rebuild, usando la imagen ya desplegada):**
```bash
# Baseline: apagar instrumentación
gcloud run services update passengers-service --region us-central1 \
  --update-env-vars OBSERVABILITY_ENABLED=false
gcloud run services update checkin-service --region us-central1 \
  --update-env-vars OBSERVABILITY_ENABLED=false

# Ejecutar el benchmark (k6/locust) contra el servicio en este estado...

# Volver a activar la instrumentación
gcloud run services update passengers-service --region us-central1 \
  --update-env-vars OBSERVABILITY_ENABLED=true
gcloud run services update checkin-service --region us-central1 \
  --update-env-vars OBSERVABILITY_ENABLED=true

# Ejecutar el mismo benchmark con instrumentación activa para comparar
```

> ⚠️ El benchmark comparativo en sí (ejecución de k6/locust, medición de p99/CPU/memoria y tabla comparativa) es responsabilidad de la Fase 4 y **no se ejecuta en este ajuste** — esta bandera solo deja preparado el mecanismo para que se pueda correr sin cambios de arquitectura ni redeploys adicionales.

---

## 👁️ Cómo Visualizar las Trazas Registradas en Jaeger

Existen **3 formas principales** para inspeccionar las trazas distribuidas capturadas por Jaeger:

### Opción 1: Jaeger UI directa (Método Gráfico Recomendado 🎨)

1. Abre en tu navegador `http://localhost:16686` (local) o la IP externa del Service `jaeger` en GKE (`http://<jaeger_IP>:16686`).
2. En el selector **Service**, elige `passengers-service` o `checkin-service`.
3. Haz clic en **Find Traces**. Aparecerá el listado de trazas recibidas.
4. Selecciona cualquier traza para abrir el diagrama interconectado (*Flamegraph*), mostrando la cascada de llamadas HTTP entre `Checkin` -> `Passengers` -> `PostgreSQL` / `Couchbase`.

### Opción 1b: Grafana Explore (datasource Jaeger)

1. Abre `http://localhost:3000` (local) o la IP externa del Service `grafana` en GKE, inicia sesión (`admin` / valor de `GF_ADMIN_PASSWORD`).
2. En el menú lateral izquierdo, selecciona **Explore** (icono de compás).
3. En el selector superior de origen de datos (*DataSource*), elige **Jaeger**.
4. Selecciona el servicio y haz clic en **Run query**.

### Opción 2: API HTTP de Jaeger (`curl` / Navegador 🌐)

Puedes consultar trazas directamente utilizando la API REST de Jaeger:

```bash
# Listar los servicios que han reportado trazas
curl -X GET "http://localhost:16686/api/services"

# Buscar trazas recientes de un servicio
curl -X GET "http://localhost:16686/api/traces?service=passengers-service&limit=20"

# Consultar los detalles de una traza por su Trace ID
curl -X GET "http://localhost:16686/api/traces/<TRACE_ID>"
```

### Opción 3: Logs del OpenTelemetry Collector (Consola 💻)

El archivo `config/otel-collector-config.yaml` (local) / `k8s/gcp/01-configmaps.yaml` (GKE) está configurado con el exportador `debug`. Cada vez que un microservicio envía un span, los detalles se imprimen en los logs del colector:

```bash
# Local
docker-compose logs -f otel-collector

# GKE
kubectl logs -n observability -l app=otel-collector -f
```

---

## 🔗 Verificación de Propagación de Contexto W3C (traceparent)

Para confirmar que el `trace_id` viaja correctamente entre `Checkin` → `Passengers` (y sus respectivas bases de datos):

```bash
# 1. Disparar un check-in que internamente llama a Passengers
curl -X POST "http://localhost:5002/checkin" \
  -H "Content-Type: application/json" \
  -d '{"passengerId":"PAS-1001","flightNumber":"AV302","seatNumber":"14C","baggageCount":1}'

# 2. Tomar el trace_id de la respuesta (header X-Correlation-ID) o de los logs JSON:
docker-compose logs checkin | grep '"trace_id"' | tail -n 1

# 3. Buscar ese trace_id en Jaeger UI (http://localhost:16686 → Search by Trace ID)
```

**Lo que debes verificar en el flame graph de Jaeger:** un único `trace_id` con spans anidados de `checkin-service` (`ProcessCheckin` → `ValidatePassengerHttp` → `PersistCheckinCouchbase`) y de `passengers-service` (`GetPassengerById`), confirmando que el header `traceparent` inyectado automáticamente por `AddHttpClientInstrumentation()`/`AddAspNetCoreInstrumentation()` propaga el contexto entre ambos servicios.

---

## 📄 Logs Estructurados JSON y Correlación con Trazas

Ambos servicios emiten logs de consola en JSON (`OtelJsonConsoleFormatter`) con esta forma:

```json
{"timestamp":"2026-08-23T18:04:12.123Z","level":"Information","category":"Program","service":"checkin-service","message":"Validating passenger PAS-1001 via Passengers service...","trace_id":"1a2b3c4d5e6f7890abcdef1234567890","span_id":"abcdef1234567890"}
```

- **Local**: `docker-compose logs -f checkin` / `docker-compose logs -f passengers`.
- **GCP**: los mismos logs llegan también al OTel Collector vía OTLP y de ahí a **Cloud Logging** (exporter `googlecloud`, pipeline de logs en `k8s/gcp/01-configmaps.yaml`).

### Plugin de Google Cloud Logging y Correlación en Grafana

El Grafana desplegado en GKE incluye el datasource **Google Cloud Logging** (plugin oficial `googlecloud-logging-datasource`), autenticado vía Workload Identity (sin llaves JSON — ver permisos requeridos más abajo). Para poder pivotear de un log en Cloud Logging hacia su traza en Jaeger usando `trace_id`, configura una vez la función **Correlations** de Grafana (no se aprovisiona por YAML porque requiere el UID real del datasource Jaeger ya creado):

1. Entra a **Administration → Plugins and data → Correlations → Add**.
2. **Label**: `Cloud Logging → Jaeger`.
3. **Target data source**: `Jaeger`. En la query, usa el trace ID como variable (`${traceId}`).
4. **Source data source**: `Google Cloud Logging`. **Results field**: el campo `trace` (o `trace_id` si extraes con una transformación `regex`/`logfmt` del `jsonPayload`).
5. Guarda. Desde ahora, cualquier resultado de Cloud Logging con ese campo mostrará un enlace que abre la traza correspondiente en Jaeger dentro de Grafana Explore.

> Se documenta como paso manual de una sola vez (vía UI) en lugar de aprobar directamente el YAML de correlación, porque la estructura exacta del "target query" depende del UID que Grafana asigna al datasource Jaeger en tiempo de ejecución — ver [documentación oficial de Grafana Correlations](https://grafana.com/docs/grafana/latest/administration/correlations/).

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

---

## ⚙️ Configuración de Workload Identity Federation (WIF) en GCP

Esta es la configuración **única y necesaria** antes de poder usar los workflows de CD. Solo se realiza una vez.

### Prerrequisito: Crear el Artifact Registry

```bash
export PROJECT_ID="tu-proyecto-gcp"
export REGION="us-central1"

# Crear repositorio en Artifact Registry
gcloud artifacts repositories create actividad-2-2 \
  --repository-format=docker \
  --location=${REGION} \
  --project=${PROJECT_ID} \
  --description="Docker images for actividad-2.2"
```

### Prerrequisito: Crear y Configurar la VM de GCE

```bash
export VM_NAME="observability-vm"
export VM_ZONE="us-central1-a"

# Crear VM (e2-medium: 2 vCPU / 4 GB RAM — recomendada para todos los contenedores)
gcloud compute instances create ${VM_NAME} \
  --zone=${VM_ZONE} \
  --machine-type=e2-medium \
  --image-family=debian-12 \
  --image-project=debian-cloud \
  --boot-disk-size=30GB \
  --tags=observability-server \
  --project=${PROJECT_ID}

# Crear regla de firewall para los puertos de los servicios de base de datos
# NOTA: Solo para uso educativo. En producción, restringir source-ranges.
# NOTA: Los puertos de observabilidad (4317/4318/8889/8888/9090/3000/16686) YA NO
# viven en esta VM — ahora corren en GKE Autopilot (ver k8s/gcp/ y observability-gke.yml).

# PowerShell: el valor de --allow DEBE ir entre comillas (las comas son separadores de array en PS)
gcloud compute firewall-rules create allow-observability-services `
  --allow="tcp:5432,tcp:8091,tcp:8092,tcp:8093,tcp:11210" `
  --target-tags=observability-server `
  --source-ranges=0.0.0.0/0 `
  --description="Allow database service ports (educational only)" `
  --project=$env:PROJECT_ID

# bash / Cloud Shell (alternativa):
# gcloud compute firewall-rules create allow-observability-services \
#   --allow="tcp:5432,tcp:8091,tcp:8092,tcp:8093,tcp:11210" \
#   --target-tags=observability-server --source-ranges=0.0.0.0/0 \
#   --project=${PROJECT_ID}

# Si la regla ya existía con los puertos de observabilidad (setup previo a este
# ajuste), actualízala para retirar los puertos que ya no corren en la VM:
# gcloud compute firewall-rules update allow-observability-services \
#   --allow="tcp:5432,tcp:8091,tcp:8092,tcp:8093,tcp:11210" \
#   --project=${PROJECT_ID}

# Instalar Docker en la VM (Método oficial y seguro)
# Ejecutar vía SSH desde tu máquina local (reemplazar VM_NAME y VM_ZONE o usar variables):
gcloud compute ssh observability-vm --zone=us-central1-a --command="curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker \$USER"

# O si ya estás conectado por SSH dentro de la VM, simplemente corre:
# curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker $USER

# Obtener la IP pública de la VM (guardar como secret GCE_VM_EXTERNAL_IP)
gcloud compute instances describe ${VM_NAME} \
  --zone=${VM_ZONE} \
  --format='get(networkInterfaces[0].accessConfigs[0].natIP)'
```

### 1. Habilitar OS Login (permite SSH con la cuenta de servicio)

```bash
# Habilitar OS Login a nivel de proyecto
gcloud compute project-info add-metadata \
  --metadata=enable-oslogin=TRUE \
  --project=${PROJECT_ID}
```

### 2. Crear el Workload Identity Pool

```bash
gcloud iam workload-identity-pools create "github-actions-pool" \
  --project=${PROJECT_ID} \
  --location="global" \
  --display-name="GitHub Actions Pool"
```

### 3. Crear el Workload Identity Provider (OIDC para GitHub)

```bash
gcloud iam workload-identity-pools providers create-oidc "github-provider" \
  --project=${PROJECT_ID} \
  --location="global" \
  --workload-identity-pool="github-actions-pool" \
  --display-name="GitHub Provider" \
  --attribute-mapping="google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.actor=assertion.actor" \
  --issuer-uri="https://token.actions.githubusercontent.com"
```

### 4. Crear el Service Account

```bash
gcloud iam service-accounts create "github-actions-sa" \
  --project=${PROJECT_ID} \
  --display-name="GitHub Actions Service Account"
```

### 5. Asignar roles al Service Account

```bash
SA_EMAIL="github-actions-sa@${PROJECT_ID}.iam.gserviceaccount.com"

# Publicar imágenes en Artifact Registry
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/artifactregistry.writer"

# Desplegar a Cloud Run
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/run.developer"

# Permitir actuar como la cuenta de servicio de Cloud Run
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/iam.serviceAccountUser"

# SSH a la VM via OS Login (Admin sin contraseña)
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/compute.osAdminLogin"

# Leer metadata de instancias de Compute Engine
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/compute.viewer"
```

### 6. Enlazar el repositorio GitHub con el Service Account

```bash
export GITHUB_REPO="TU_ORG_O_USUARIO/TU_REPO"  # ej: "miusuario/actividad-2.2"

PROJECT_NUMBER=$(gcloud projects describe ${PROJECT_ID} --format='value(projectNumber)')

gcloud iam service-accounts add-iam-policy-binding \
  "github-actions-sa@${PROJECT_ID}.iam.gserviceaccount.com" \
  --project=${PROJECT_ID} \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/${PROJECT_NUMBER}/locations/global/workloadIdentityPools/github-actions-pool/attribute.repository/${GITHUB_REPO}"
```

### 7. Obtener los valores para los GitHub Secrets

```bash
# GCP_WIF_PROVIDER
gcloud iam workload-identity-pools providers describe "github-provider" \
  --project=${PROJECT_ID} \
  --location="global" \
  --workload-identity-pool="github-actions-pool" \
  --format="value(name)"

# GCP_SERVICE_ACCOUNT
echo "github-actions-sa@${PROJECT_ID}.iam.gserviceaccount.com"

# GCE_VM_EXTERNAL_IP
gcloud compute instances describe ${VM_NAME} \
  --zone=${VM_ZONE} \
  --format='get(networkInterfaces[0].accessConfigs[0].natIP)'
```

Copia los valores obtenidos y agrégalos como secrets en GitHub: **Settings → Secrets and variables → Actions**.

---

## ⚠️ Permisos GCP Adicionales Requeridos para GKE (pendientes de autorización)

Los siguientes comandos **no han sido ejecutados** — quedan documentados en el orden en que deben aplicarse para que `observability-gke.yml` funcione. Deben ejecutarse **una sola vez** con una cuenta que tenga privilegios de IAM sobre el proyecto (p. ej. `gcloud auth login` con el usuario propietario del proyecto), **antes** de disparar ese workflow por primera vez.

```bash
export PROJECT_ID="tu-proyecto-gcp"
SA_EMAIL="github-actions-sa@${PROJECT_ID}.iam.gserviceaccount.com"

# 1. Habilitar el servicio de administración de APIs (necesario para que el
#    propio workflow pueda habilitar container.googleapis.com / cloudresourcemanager.googleapis.com)
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/serviceusage.serviceUsageAdmin"

# 2. Crear y administrar el clúster GKE Autopilot
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/container.admin"

# 3. Crear la Service Account de Grafana (grafana-cloudlogging-sa) y enlazarla
#    con Workload Identity
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/iam.serviceAccountAdmin"

# 4. Otorgar el rol roles/logging.viewer a la Service Account de Grafana
#    (permite al SA de GitHub Actions modificar políticas IAM a nivel de proyecto)
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/resourcemanager.projectIamAdmin"
```

> **Nota de seguridad:** `roles/resourcemanager.projectIamAdmin` es un rol amplio (permite modificar bindings IAM de cualquier miembro en el proyecto). Para un entorno productivo, se recomienda reemplazarlo por un [rol personalizado](https://cloud.google.com/iam/docs/creating-custom-roles) acotado únicamente a `resourcemanager.projects.setIamPolicy` sobre el binding específico de `roles/logging.viewer`. Se documenta así, sin sesgo, para que decidas conscientemente el trade-off entre simplicidad y mínimo privilegio.

---

## 🚫 Alcance y Exclusiones

- **Solo GCP.** El enunciado del laboratorio menciona una ruta alternativa en AWS (ECS Fargate + AWS X-Ray/Tempo). Esta actividad **no la implementa**: el repositorio, los workflows y los secrets están construidos exclusivamente alrededor de GCP (Cloud Run + GKE Autopilot + GCE), que es el proveedor que ya estaba en uso antes de este ajuste.
- **Tempo fue reemplazado por Jaeger** (no coexisten) para cumplir literalmente con "Trazas: Jaeger UI (GCP)" del enunciado.
- **PostgreSQL y Couchbase permanecen en la VM de GCE.** Solo se migró a GKE el plano de observabilidad (Collector, Jaeger, Prometheus, Grafana), que es lo que exige la Fase 2 del enunciado.
- **Dashboards de Grafana (6 paneles), correlación logs↔trazas en profundidad y benchmark de overhead (k6/locust)** quedan fuera del alcance de este ajuste — la instrumentación, el Collector y los datasources necesarios ya quedan preparados (incluyendo métricas internas del Collector en `:8888`, `OpenTelemetry.Instrumentation.Runtime` para overhead de CPU/memoria, y la bandera `OBSERVABILITY_ENABLED` para alternar entre "con instrumentación" y "sin instrumentación" — ver sección [Bandera OBSERVABILITY_ENABLED](#-bandera-observability_enabled--benchmark-de-overhead-fase-4)) para que puedan completarse sin requerir cambios de arquitectura.

