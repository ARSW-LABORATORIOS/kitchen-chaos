terraform {
  required_version = ">= 1.5"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    time = {
      source  = "hashicorp/time"
      version = "~> 0.12"
    }
  }
}

provider "azurerm" {
  features {}

  subscription_id = var.subscription_id

  # Una suscripción nueva de Azure for Students no trae registrados estos servicios;
  # Terraform los registra solo la primera vez.
  resource_provider_registrations = "core"
  resource_providers_to_register = [
    "Microsoft.App",
    "Microsoft.OperationalInsights",
    "Microsoft.ContainerRegistry",
  ]
}
