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
  - Credenciales de administrador: leídas de variables de entorno (`CB_ADMIN_USER`, `CB_ADMIN_PASSWORD`), con fallback a `Administrator` / `password` para desarrollo local.
  - Creación automática del bucket **`checkin_bucket`** (RAM 256MB) si no existe.
  - Utilización explícita del **scope por defecto (`_default`)** y **collection por defecto (`_default`)**.
- **Observabilidad e Interfaz Visual Integrada**:
  - **Grafana UI (`:3000`)**: Aprovisionamiento automático de datasources para Prometheus y Grafana Tempo.
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
| **Grafana Tempo API** | `http://localhost:3200` | Backend API de trazas distribuidas |
| **Couchbase Console** | `http://localhost:8091` | Consola NoSQL (`Administrator` / `password`) |
| **OTLP Collector gRPC** | `localhost:4317` | Puerto de recepción de OpenTelemetry |
| **OTLP Collector Metrics** | `http://localhost:8889/metrics` | Exporter de métricas en formato Prometheus |
| **PostgreSQL** | `localhost:5432` | Servidor de base de datos relacional |

---

## 🤖 CI/CD con GitHub Actions

### Arquitectura de los Workflows

```text
┌──────────────────────────────────────────────────────────────────────────────────┐
│  Cambios en config/** o init-couchbase.sh                                        │
│                │                                                                  │
│                ▼                                                                  │
│  ┌─────────────────────────────┐                                                 │
│  │  prerequisites.yml          │  ──► GCE VM (e2-medium)                         │
│  │  Deploy Infrastructure      │      PostgreSQL, Couchbase, OTel,               │
│  │  (workflow_dispatch / push) │      Tempo, Prometheus, Grafana                 │
│  └─────────────────────────────┘                                                 │
│                                                                                  │
│  Cambios en Passengers/**          Cambios en Checkin/**                         │
│       │                                    │                                     │
│       ▼                                    ▼                                     │
│  ┌──────────────┐                ┌──────────────────┐                            │
│  │ passengers   │                │   checkin-ci.yml │                            │
│  │ -ci.yml      │                │   Build & Test   │                            │
│  │ Build & Test │                └────────┬─────────┘                            │
│  └──────┬───────┘                         │ (on success)                         │
│         │ (on success)                    ▼                                      │
│         ▼                       ┌──────────────────┐                             │
│  ┌──────────────────┐           │ checkin-cd.yml   │                             │
│  │ passengers-cd.yml│           │ 1. Build & Push  │──► Artifact Registry        │
│  │ 1. Build & Push  │──────────►│    Docker Image  │                             │
│  │    Docker Image  │           │ 2. Deploy to     │──► Cloud Run                │
│  │ 2. Deploy to     │           │    Cloud Run     │    (Couchbase → GCE VM)     │
│  │    Cloud Run     │           └──────────────────┘                             │
│  │    (PG → GCE VM) │                                                            │
│  └──────────────────┘                                                            │
└──────────────────────────────────────────────────────────────────────────────────┘
```

### Descripción de los Workflows

| Archivo | Trigger | Descripción |
| :--- | :--- | :--- |
| `prerequisites.yml` | push a `main` con cambios en `config/**`, `init-couchbase.sh` o `docker-compose.yml`; también manual (`workflow_dispatch`) | Despliega/actualiza PostgreSQL, Couchbase (+ init), OTel Collector, Tempo, Prometheus y Grafana en una VM de GCE vía SSH |
| `passengers-ci.yml` | push / PR a `main` con cambios en `Passengers/**` | Restaura dependencias, compila y corre pruebas unitarias del servicio Passengers |
| `passengers-cd.yml` | `workflow_run` al completar `Passengers CI` con éxito | Construye y publica imagen Docker a Artifact Registry; despliega a Cloud Run |
| `checkin-ci.yml` | push / PR a `main` con cambios en `Checkin/**` | Restaura dependencias, compila y corre pruebas unitarias del servicio Checkin |
| `checkin-cd.yml` | `workflow_run` al completar `Checkin CI` con éxito | Construye y publica imagen Docker a Artifact Registry; despliega a Cloud Run |

### Orden de Ejecución Obligatorio

```
1. prerequisites.yml        ← SIEMPRE primero (al menos una vez)
2. passengers-cd.yml        ← Segundo (Checkin depende de la URL de Passengers)
3. checkin-cd.yml           ← Tercero
```

> **Primera vez:** Después de que `passengers-cd.yml` termine, copia la URL del servicio Cloud Run mostrada en el Step Summary y agrégala como secret `PASSENGERS_SERVICE_URL`. Esto es necesario solo una vez.

---

### 🔑 GitHub Secrets Requeridos

Agrega los siguientes secrets en: **Settings → Secrets and variables → Actions → New repository secret**

#### Autenticación GCP
| Secret | Ejemplo / Descripción |
| :--- | :--- |
| `GCP_PROJECT_ID` | `mi-proyecto-123456` |
| `GCP_WIF_PROVIDER` | `projects/123456789/locations/global/workloadIdentityPools/github-pool/providers/github-provider` |
| `GCP_SERVICE_ACCOUNT` | `github-actions-sa@mi-proyecto-123456.iam.gserviceaccount.com` |

#### VM de GCE
| Secret | Ejemplo / Descripción |
| :--- | :--- |
| `GCE_VM_NAME` | `observability-vm` |
| `GCE_VM_ZONE` | `us-central1-a` |
| `GCE_VM_EXTERNAL_IP` | IP pública estática de la VM (ej. `34.68.100.200`) |

#### Bases de Datos
| Secret | Valor por defecto local | Descripción |
| :--- | :--- | :--- |
| `CB_ADMIN_USER` | `Administrator` | Usuario admin de Couchbase |
| `CB_ADMIN_PASSWORD` | `password` | Contraseña admin de Couchbase |
| `POSTGRES_USER` | `postgres` | Usuario de PostgreSQL |
| `POSTGRES_PASSWORD` | `postgres` | Contraseña de PostgreSQL |
| `POSTGRES_DB` | `passengers_db` | Nombre de la base de datos |
| `GF_ADMIN_PASSWORD` | `admin` | Contraseña admin de Grafana |

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

El archivo `config/otel-collector-config.yaml` está configurado con el exportador `debug` en modo `detailed`. Cada vez que un microservicio envía un span, los detalles se imprimen en los logs del colector:

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

# Crear regla de firewall para los puertos de los servicios
# NOTA: Solo para uso educativo. En producción, restringir source-ranges.
gcloud compute firewall-rules create allow-observability-services \
  --allow=tcp:5432,tcp:8091,tcp:8092,tcp:8093,tcp:11210,tcp:4317,tcp:4318,tcp:8889,tcp:9090,tcp:3000,tcp:3200 \
  --target-tags=observability-server \
  --source-ranges="0.0.0.0/0" \
  --description="Allow observability service ports (educational only)" \
  --project=${PROJECT_ID}

# Instalar Docker en la VM
gcloud compute ssh ${VM_NAME} --zone=${VM_ZONE} --command="
  sudo apt-get update -qq &&
  sudo apt-get install -y ca-certificates curl &&
  sudo install -m 0755 -d /etc/apt/keyrings &&
  sudo curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc &&
  sudo chmod a+r /etc/apt/keyrings/docker.asc &&
  echo 'deb [arch=\$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian \$(. /etc/os-release && echo \"\$VERSION_CODENAME\") stable' | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null &&
  sudo apt-get update -qq &&
  sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin &&
  sudo usermod -aG docker \$USER
"

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

# SSH a la VM via OS Login
gcloud projects add-iam-policy-binding ${PROJECT_ID} \
  --member="serviceAccount:${SA_EMAIL}" \
  --role="roles/compute.osLogin"

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
