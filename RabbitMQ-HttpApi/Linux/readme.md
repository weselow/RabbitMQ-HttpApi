# Как публиковать и запускать приложение

## Как запустить

```bash
sudo nano /etc/systemd/system/rabbitmq-httpapi.service
sudo systemctl daemon-reload
sudo systemctl start rabbitmq-httpapi.service
sudo systemctl status rabbitmq-httpapi.service
```

## Варианты запуска

**Вариант 1: `ExecStart=/opt/rabbitmq-httpapi/RabbitMQ-HttpApi`**

*   **Что это означает:**
    *   Этот вариант предполагает, что вы опубликовали ваше приложение как **самодостаточное (self-contained)**.
    *   При самодостаточной публикации создается исполняемый файл (в данном случае `RabbitMQ-HttpApi` без расширения для Linux), который включает в себя среду выполнения .NET и все зависимости вашего приложения.
    *   Этот исполняемый файл можно запустить напрямую, как любую другую нативную программу Linux.
*   **Как этого достичь при публикации:**
    При выполнении команды `dotnet publish` нужно указать соответствующие флаги:
    ```bash
    dotnet publish ./RabbitMQ-HttpApi/RabbitMQ-HttpApi.csproj --configuration Release --output /opt/rabbitmq-httpapi -r linux-x64 --self-contained true
    ```
    Или, если вы хотите, чтобы исполняемый файл был один (включая DLL сборки в него, для .NET 6+ это по умолчанию при `--self-contained true` и публикации для конкретного RID):
    ```bash
    dotnet publish ./RabbitMQ-HttpApi/RabbitMQ-HttpApi.csproj --configuration Release --output /opt/rabbitmq-httpapi -r linux-x64 --self-contained true /p:PublishSingleFile=true
    ```
    *   `-r linux-x64` (Runtime Identifier - RID): Указывает, для какой целевой платформы собирается самодостаточное приложение (например, `linux-x64`, `linux-arm64`, `win-x64`).
    *   `--self-contained true`: Ключевой флаг для создания самодостаточного приложения.
    *   `/p:PublishSingleFile=true` (опционально): Упаковывает все в один исполняемый файл. Без него у вас будет исполняемый файл и рядом все необходимые DLL.
*   **Преимущества:**
    *   **Простота развертывания:** На целевом сервере не обязательно должен быть установлен .NET SDK или Runtime (если версия .NET в приложении совпадает или новее той, что есть на сервере, для framework-dependent это не так). Для self-contained — .NET Runtime не нужен на сервере вообще.
    *   **Изоляция:** Приложение использует ту версию .NET, с которой было скомпилировано, что уменьшает проблемы совместимости.
*   **Недостатки:**
    *   **Больший размер публикации:** Так как среда выполнения .NET включена в пакет, артефакты будут больше.
    *   Если у вас много .NET приложений на одном сервере, каждое будет нести свою копию среды выполнения (если они self-contained).

**Вариант 2: `ExecStart=/usr/bin/dotnet /opt/rabbitmq-httpapi/RabbitMQ-HttpApi.dll`**

*   **Что это означает:**
    *   Этот вариант предполагает, что вы опубликовали ваше приложение как **зависимое от фреймворка (framework-dependent)**.
    *   При такой публикации создается `*.dll` файл вашего приложения (например, `RabbitMQ-HttpApi.dll`) и другие зависимые DLL.
    *   Для запуска этого приложения на целевом сервере **должна быть установлена соответствующая (или более новая совместимая) версия .NET Runtime**.
    *   Команда `dotnet your_app.dll` использует глобально установленный `dotnet` для запуска вашей DLL.
*   **Как этого достичь при публикации:**
    Это поведение по умолчанию для `dotnet publish`, если не указан `--self-contained true`:
    ```bash
    dotnet publish ./RabbitMQ-HttpApi/RabbitMQ-HttpApi.csproj --configuration Release --output /opt/rabbitmq-httpapi
    ```
*   **Преимущества:**
    *   **Меньший размер публикации:** Пакет содержит только код вашего приложения и его прямые зависимости, так как среда выполнения .NET используется системная.
    *   Если на сервере несколько .NET приложений, они могут использовать одну и ту же установленную среду выполнения .NET (экономия места, централизованное обновление Runtime).
*   **Недостатки:**
    *   **Зависимость от установленного .NET Runtime:** На сервере должна быть установлена нужная версия .NET Runtime. Если ее нет или версия несовместима, приложение не запустится.
    *   Потенциальные проблемы, если системный .NET Runtime обновляется до несовместимой версии (хотя .NET старается поддерживать обратную совместимость).

**Как в итоге должен запускаться ваш проект?**

**Для вашего CI/CD workflow (`deploy-self-hosted.yml`), где сборка и публикация происходят на самом self-hosted runner'е (целевом сервере):**

*   **Если вы используете `dotnet publish ... --self-contained true -r linux-x64 ...` в workflow:**
    Тогда в файле `rabbitmq-httpapi.service` **правильным будет:**
    `ExecStart=/opt/rabbitmq-httpapi/RabbitMQ-HttpApi`
    (И не забудьте `sudo chmod +x /opt/rabbitmq-httpapi/RabbitMQ-HttpApi` в workflow после публикации, если права на выполнение не установились автоматически).

*   **Если вы используете `dotnet publish ...` (без `--self-contained true`) в workflow:**
    Тогда в файле `rabbitmq-httpapi.service` **правильным будет:**
    `ExecStart=/usr/bin/dotnet /opt/rabbitmq-httpapi/RabbitMQ-HttpApi.dll`
    (И убедитесь, что на сервере установлен .NET Runtime нужной версии).

**Рекомендация для вашего случая (self-hosted runner на целевом сервере):**

Оба подхода рабочие. Выбор зависит от ваших предпочтений и политики управления .NET Runtime на сервере.

1.  **Framework-dependent (запуск через `dotnet myapp.dll`):**
    *   **Проще команда `dotnet publish`** в workflow (меньше флагов).
    *   **Меньше размер артефактов**, которые runner будет перемещать/создавать на диске.
    *   **Требует, чтобы .NET Runtime был установлен и поддерживался на сервере.** Поскольку runner уже работает на этом сервере и, скорее всего, .NET SDK там уже есть (для шагов сборки), то и Runtime там будет. Это делает данный вариант довольно привлекательным в вашем случае.
    *   Если вы обновляете .NET в проекте, вам нужно будет обновить и .NET Runtime на сервере.

2.  **Self-contained (запуск прямого исполняемого файла `myapp`):**
    *   **Более сложная команда `dotnet publish`** (нужно указать RID и `--self-contained`).
    *   **Больше размер артефактов.**
    *   **Меньше зависимостей от того, что установлено на сервере** (в плане .NET Runtime для *этого конкретного* приложения).
    *   Может быть предпочтительнее, если вы хотите точно контролировать версию Runtime для каждого приложения и избежать конфликтов, или если установка глобального Runtime затруднена.

**Что я бы выбрал для вашего `deploy-self-hosted.yml`?**

Учитывая, что .NET SDK уже должен быть на self-hosted runner'е для выполнения шагов сборки, **вариант с framework-dependent публикацией и запуском через `dotnet YourApp.dll` выглядит немного проще и логичнее для этого конкретного сценария.**

**Если вы выберете framework-dependent:**

*   **Workflow `deploy-self-hosted.yml` (шаг публикации):**
    ```yaml
    - name: Publish application
      run: dotnet publish ${{ env.PROJECT_PATH }} --configuration Release --output ${{ env.DEPLOY_PATH }} --no-build
    ```
*   **Файл `rabbitmq-httpapi.service`:**
    ```ini
    ExecStart=/usr/bin/dotnet /opt/rabbitmq-httpapi/RabbitMQ-HttpApi.dll
    ```
    (Убедитесь, что `RabbitMQ-HttpApi.dll` — это правильное имя вашей основной DLL после публикации. Обычно оно совпадает с именем проекта).

Если вы выберете self-contained, соответствующим образом измените команду `publish` и `ExecStart`.

**Важно:** Выберите один подход и придерживайтесь его как в команде `dotnet publish` вашего workflow, так и в `ExecStart` вашего файла `systemd`. Несоответствие приведет к тому, что сервис не сможет запуститься.