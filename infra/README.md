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


## Despliegue automático (GitHub Actions)

El archivo `.github/workflows/deploy.yml` prueba y despliega solo cada vez que se mezcla algo en `develop`
(también se puede lanzar a mano desde la pestaña Actions). Viene **apagado**: solo corren las pruebas hasta que
se crea la variable `AZURE_DEPLOY_ENABLED = true` en GitHub.

Cómo funciona: construye la imagen con el hash del commit, la sube al registro y actualiza la Container App.
Como la app tiene una sola revisión activa y una sonda `/ready`, si la versión nueva no arranca, Azure no le pasa el
tráfico y la anterior sigue sirviendo (el flujo queda en rojo en GitHub).

### Configuración (una sola vez)

1. **Login de Google en Azure.** La app nueva exige Google y no arranca sin él. En `terraform.tfvars` agrega
   `google_client_id` y `google_client_secret` (ese archivo no se sube al repositorio), y aplica:

   ```bash
   terraform plan
   terraform apply
   ```

   Esto también crea la clave del JWT y activa `ASPNETCORE_FORWARDEDHEADERS_ENABLED`. A partir de ahora Terraform ya
   no toca la imagen (la maneja GitHub Actions).

2. **Google Cloud Console.** En el cliente OAuth agrega como URI de redirección
   `https://<url_del_juego>/auth/google/callback` (la URL sale de `terraform output url_del_juego`).

3. **Identidad para GitHub (sin contraseñas, con OIDC).** Reemplaza lo que va entre `< >`:

   ```bash
   az ad app create --display-name kitchen-chaos-github --query appId -o tsv
   az ad sp create --id <appId>
   az ad app federated-credential create --id <appId> --parameters '{"name":"github-develop","issuer":"https://token.actions.githubusercontent.com","subject":"repo:ARSW-LABORATORIOS/kitchen-chaos:ref:refs/heads/develop","audiences":["api://AzureADTokenExchange"]}'
   az role assignment create --assignee <appId> --role Contributor --scope /subscriptions/<subscription_id>/resourceGroups/rg-kitchenchaos
   az role assignment create --assignee <appId> --role AcrPush --scope $(az acr show --name <registro> --query id -o tsv)
   ```

   Algunas cuentas de estudiante no dejan crear registros de aplicaciones. Si `az ad app create` da error de permisos,
   hay que pedirlo al profesor o desplegar a mano (ver los pasos de arriba).

4. **GitHub** (Settings > Secrets and variables > Actions):

   | Tipo | Nombre | Valor |
   |---|---|---|
   | Secret | `AZURE_CLIENT_ID` | el `appId` del paso anterior |
   | Secret | `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
   | Secret | `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
   | Variable | `AZURE_ACR_NAME` | el nombre del registro (`terraform output registro`) |
   | Variable | `AZURE_APP_NAME` | `ca-kitchenchaos` |
   | Variable | `AZURE_RESOURCE_GROUP` | `rg-kitchenchaos` |
   | Variable | `AZURE_DEPLOY_ENABLED` | `true` (se crea al final, cuando todo lo demás esté listo) |

### Cosas a tener en cuenta

- Cada despliegue reinicia la app: se cortan las partidas en curso (las salas viven en memoria) y todos tienen que
  volver a iniciar sesión.
- Para que un commit no dispare nada, agrega `[skip ci]` al mensaje. Los cambios solo de `.md`, `docs/`, `infra/` y
  `observability/` no despliegan.
- Para apagar el despliegue automático (por ejemplo antes de la presentación) borra la variable `AZURE_DEPLOY_ENABLED`
  o cámbiale el valor.
