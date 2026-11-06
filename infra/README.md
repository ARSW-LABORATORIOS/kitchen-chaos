# Despliegue en Azure con Terraform

Crea en Azure: un grupo de recursos, un registro de imágenes (Container Registry), un espacio de logs
(Log Analytics), un entorno de Container Apps y la Container App del juego con **una sola réplica**
(las salas y los locks viven en memoria, con dos réplicas no se compartirían).

## Requisitos

- [Terraform](https://developer.hashicorp.com/terraform/install) y [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- Docker Desktop abierto
- Sesión iniciada: `az login`

## Pasos

El primer `apply` no crea la Container App, porque todavía no hay imagen en el registro. Se hace en dos pasos.

1. Preparar las variables (desde la carpeta `infra/`):

   ```bash
   cp terraform.tfvars.example terraform.tfvars
   ```

   Edita `terraform.tfvars` y pon tu `subscription_id` (`az account show --query id -o tsv`).
   La región debe ser una permitida por la suscripción de estudiante.

2. Crear la base: grupo de recursos, registro y entorno.

   ```bash
   terraform init
   terraform plan
   terraform apply
   ```

   Revisa el `plan` antes de aplicar. `apply` pide escribir `yes`.

3. Construir y subir la imagen al registro (desde la raíz del repositorio):

   ```bash
   az acr login --name <registro>
   docker build -t <registro_servidor>/kitchen-chaos:v1 .
   docker push <registro_servidor>/kitchen-chaos:v1
   ```

   `<registro>` y `<registro_servidor>` salen de `terraform output`.

4. Crear la Container App: en `terraform.tfvars` pon `image_tag = "v1"` y vuelve a aplicar.

   ```bash
   terraform apply
   terraform output url_del_juego
   ```

5. Abre la URL: debe cargar el juego, y `<url>/ready` debe responder 200.

## Actualizar el juego

Sube una imagen con otra etiqueta (`v2`), cambia `image_tag` y aplica de nuevo. Las partidas en curso se cortan.

## Leer las métricas

`/metrics` exige el token que Terraform generó:

```bash
terraform output -raw token_de_metricas
```

Se envía como `Authorization: Bearer <token>`.

## Apagar todo (para no gastar crédito)

```bash
terraform destroy
```

## Notas

- `terraform.tfvars` y el estado (`*.tfstate`) no se suben al repositorio: el estado guarda el token de `/metrics`.
- El estado queda en tu computador. Quien despliegue desde otro equipo empezaría sin él; para trabajar en grupo
  habría que moverlo a un Azure Storage.
