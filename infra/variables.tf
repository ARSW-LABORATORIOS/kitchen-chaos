variable "subscription_id" {
  description = "Id de la suscripción de Azure (az account show --query id -o tsv)."
  type        = string
}

variable "location" {
  description = "Región de Azure. Azure for Students solo permite unas pocas; estas son las de esta cuenta."
  type        = string
  default     = "eastus"

  validation {
    condition     = contains(["southcentralus", "eastus", "brazilsouth", "canadacentral", "chilecentral"], var.location)
    error_message = "La región debe ser una de las permitidas por la suscripción: southcentralus, eastus, brazilsouth, canadacentral o chilecentral."
  }
}

variable "prefix" {
  description = "Prefijo de los nombres de los recursos. Solo minúsculas y números."
  type        = string
  default     = "kitchenchaos"

  validation {
    condition     = can(regex("^[a-z0-9]{3,20}$", var.prefix))
    error_message = "El prefijo debe tener entre 3 y 20 caracteres, solo minúsculas y números."
  }
}

variable "image_tag" {
  description = "Etiqueta de la imagen del juego en el registro. Vacía = todavía no se crea la Container App (primer paso, antes de subir la imagen)."
  type        = string
  default     = ""
}
