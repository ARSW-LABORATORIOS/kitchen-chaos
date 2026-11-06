# Kitchen Chaos en Azure Container Apps.
#
# Se aplica en dos pasos (ver infra/README.md):
#   1. image_tag vacío: crea el grupo de recursos, el registro de imágenes y el entorno.
#   2. Se sube la imagen al registro y se vuelve a aplicar con image_tag: crea la Container App.
# Así la Container App nunca intenta bajar una imagen que todavía no existe.

resource "random_string" "sufijo" {
  length  = 5
  upper   = false
  special = false
}

# Token con el que se protege /metrics (el juego exige "Authorization: Bearer <token>").
resource "random_password" "metrics_token" {
  length  = 32
  special = false
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${var.prefix}"
  location = var.location
}

# Logs de la Container App. El tope diario evita que el registro de logs gaste crédito sin querer.
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${var.prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  daily_quota_gb      = 0.5
}

# Registro privado donde se guarda la imagen del juego. El nombre debe ser único en todo Azure.
resource "azurerm_container_registry" "main" {
  name                = "${var.prefix}${random_string.sufijo.result}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Basic"
  admin_enabled       = false
}

# Identidad con la que la Container App baja la imagen, sin guardar contraseñas.
resource "azurerm_user_assigned_identity" "pull" {
  name                = "id-${var.prefix}-pull"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.pull.principal_id
}

# Los permisos tardan un poco en propagarse; sin esta espera la primera descarga de la imagen puede fallar.
resource "time_sleep" "esperar_permisos" {
  depends_on      = [azurerm_role_assignment.acr_pull]
  create_duration = "60s"
}

resource "azurerm_container_app_environment" "main" {
  name                       = "cae-${var.prefix}"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id

  # Azure crea este perfil por su cuenta; se declara para que Terraform no intente quitarlo en cada apply.
  workload_profile {
    name                  = "Consumption"
    workload_profile_type = "Consumption"
  }
}

resource "azurerm_container_app" "game" {
  count = var.image_tag == "" ? 0 : 1

  name                         = "ca-${var.prefix}"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = azurerm_resource_group.main.name
  revision_mode                = "Single"

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.pull.id]
  }

  registry {
    server   = azurerm_container_registry.main.login_server
    identity = azurerm_user_assigned_identity.pull.id
  }

  secret {
    name  = "metrics-token"
    value = random_password.metrics_token.result
  }

  ingress {
    external_enabled = true
    target_port      = 8080
    transport        = "auto" # SignalR usa WebSocket y negocia solo

    traffic_weight {
      latest_revision = true
      percentage      = 100
    }
  }

  template {
    # Una sola réplica: las salas y los locks viven en memoria, con dos réplicas no se compartirían.
    min_replicas = 1
    max_replicas = 1

    container {
      name   = "juego"
      image  = "${azurerm_container_registry.main.login_server}/kitchen-chaos:${var.image_tag}"
      cpu    = 0.25
      memory = "0.5Gi"

      env {
        name        = "Metrics__Token"
        secret_name = "metrics-token"
      }

      startup_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/ready"
      }

      liveness_probe {
        transport = "HTTP"
        port      = 8080
        path      = "/ready"
      }
    }
  }

  depends_on = [time_sleep.esperar_permisos]
}
