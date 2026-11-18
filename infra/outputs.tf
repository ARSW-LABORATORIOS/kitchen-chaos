output "registro" {
  description = "Nombre del registro de imágenes (para az acr login)."
  value       = azurerm_container_registry.main.name
}

output "registro_servidor" {
  description = "Dirección del registro, usada para etiquetar y subir la imagen."
  value       = azurerm_container_registry.main.login_server
}

output "url_del_juego" {
  description = "Dirección pública del juego (vacía hasta que se crea la Container App)."
  value       = length(azurerm_container_app.game) > 0 ? "https://${azurerm_container_app.game[0].ingress[0].fqdn}" : ""
}

output "token_de_metricas" {
  description = "Token para leer /metrics (Authorization: Bearer <token>)."
  value       = random_password.metrics_token.result
  sensitive   = true
}
