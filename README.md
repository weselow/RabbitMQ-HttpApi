# RabbitMQ Minimal API Gateway

[![.NET](https://github.com/actions/workflow_status.svg?branch=main&event=push&workflow=.NET)](https://github.com/weselow/RabbitMQ-HttpApi/actions/workflows/dotnet.yml) <!-- Замените на ваш бейдж CI, если есть -->
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

Простой и легковесный Minimal API на ASP.NET Core 8 для взаимодействия с очередями сообщений RabbitMQ. Предоставляет HTTP-эндпоинты для отправки и получения сообщений, обеспечивая базовую авторизацию по токену.

## Особенности

*   **Отправка сообщений:** `POST /add/{queue}` для публикации сообщений в указанную очередь.
*   **Получение сообщений:** `GET /get/{queue}` для извлечения одного сообщения из очереди.
*   **Авторизация:** Защита эндпоинтов с помощью Bearer-токена в заголовке `Authorization`.
*   **Конфигурация:** Все параметры (RabbitMQ, API) настраиваются через `appsettings.json` или переменные окружения.
*   **Swagger/OpenAPI:** Автоматически генерируемая документация API доступна по адресу `/swagger`.
*   **Поддержка форматов:** Принимает и отдает сообщения в `application/json` и `text/plain`.
*   **Надежность RabbitMQ:**
    *   Сообщения публикуются как `persistent` (delivery_mode = 2).
    *   При получении используется `autoAck: false` с последующим явным `BasicAck` после успешной отправки ответа клиенту.
    *   Автоматическое создание очередей (durable) при обращении, если они не существуют.
*   **Современный стек:** Построен на .NET 8 и ASP.NET Core Minimal APIs.

## Запуск как сервиса на Linux VPS

Приложение можно развернуть и запустить как systemd-сервис на VPS с Linux. 
Подробная инструкция по настройке и публикации сервиса приведена в [readme.md в папке Linux](RabbitMQ-HttpApi/Linux/readme.md).


## Запуск с использованием Docker

Для запуска приложения с использованием Docker, выполните следующую команду:
```bash
docker compose up --build
```

## Структура проекта
```
/RabbitMqApi
├── RabbitMqApi.csproj        # Файл проекта
├── Program.cs                # Основной файл настройки и регистрации эндпоинтов
├── appsettings.json          # Конфигурация приложения
├── appsettings.Development.json # Конфигурация для среды разработки
├── Configuration/
│   ├── ApiConfig.cs          # Модель конфигурации API
│   └── RabbitMqConfig.cs     # Модель конфигурации RabbitMQ
├── Middleware/
│   └── AuthMiddleware.cs     # Middleware для авторизации по токену
└── Services/
    └── RabbitService.cs      # Сервис для взаимодействия с RabbitMQ
```

## Требования

*   [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) или новее
*   Работающий экземпляр [RabbitMQ](https://www.rabbitmq.com/download.html)

## Настройка и Запуск

1.  **Клонируйте репозиторий:**
```bash
    git clone https://github.com/weselow/RabbitMQ-HttpApi.git
    cd RabbitMQ-HttpApi/RabbitMqApi
```
2.  **Сконфигурируйте `appsettings.json` (или `appsettings.Development.json`):**
    Откройте `appsettings.Development.json` (рекомендуется для локальной разработки, чтобы не коммитить секреты) или `appsettings.json` и укажите ваши параметры:{
```
"Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "RabbitMqApi.Services.RabbitService": "Debug" // Для детального лога RabbitService
    }
  },
  "RabbitMQ": {
    "Host": "localhost", // Хост вашего RabbitMQ сервера
    "Port": 5672,
    "Username": "guest", // Имя пользователя RabbitMQ
    "Password": "guest", // Пароль RabbitMQ
        "VirtualHost": "/"
      },
      "Api": {
        "AuthToken": "YOUR_SUPER_SECRET_TOKEN", // Замените на ваш надежный токен
        "Port": 5000 // Порт, на котором будет работать API
      }
    }
}
```
3.  **Запустите приложение:** Из корневой папки проекта (`RabbitMqApi`):
 ```bash
 dotnet run   
 ```
 
 Или через вашу IDE (Visual Studio, Rider, VS Code).

    API будет доступен по адресу `http://localhost:{Api:Port}` (например, `http://localhost:5000`).
    Swagger UI: `http://localhost:{Api:Port}/swagger`.

## Использование API

Все запросы к API требуют наличия заголовка `Authorization`.

**Заголовок авторизации:**
`Authorization: Bearer YOUR_SUPER_SECRET_TOKEN`

Где `YOUR_SUPER_SECRET_TOKEN` – это значение, указанное в `Api:AuthToken` в файле конфигурации.

### 1. Отправка сообщения в очередь

*   **Эндпоинт:** `POST /add/{queue}`
*   **Метод:** `POST`
*   **Параметр пути:**
    *   `queue` (string, required): Имя очереди RabbitMQ.
*   **Заголовки:**
    *   `Authorization: Bearer {token}`
    *   `Content-Type: application/json` или `Content-Type: text/plain`
*   **Тело запроса:** Содержимое сообщения, которое будет отправлено в очередь.
    *   Для `application/json`: валидный JSON объект или строка.
    *   Для `text/plain`: обычный текст.
*   **Ответы:**
    *   `200 OK`: Сообщение успешно опубликовано. Тело ответа: `"Message published successfully."`
    *   `400 Bad Request`: Ошибка в запросе (например, пустое тело, невалидный JSON).
    *   `401 Unauthorized`: Неверный или отсутствующий токен авторизации.
    *   `500 Internal Server Error`: Внутренняя ошибка сервера (например, проблема с RabbitMQ).

**Пример (curl):**
```
curl -X POST "http://localhost:5000/add/my_test_queue" \
    -H "Authorization: Bearer YOUR_SUPER_SECRET_TOKEN" \
    -H "Content-Type: application/json" \
    -d '{ "message": "Hello RabbitMQ from API!", "id": 123 }'

curl -X POST "http://localhost:5000/add/another_queue" \
    -H "Authorization: Bearer YOUR_SUPER_SECRET_TOKEN" \
    -H "Content-Type: text/plain" \
    -d "Простое текстовое сообщение"
```

### 2. Получение сообщения из очереди

*   **Эндпоинт:** `GET /get/{queue}`
*   **Метод:** `GET`
*   **Параметр пути:**
    *   `queue` (string, required): Имя очереди RabbitMQ.
*   **Заголовки:**
    *   `Authorization: Bearer {token}`
    *   `Accept: application/json` или `Accept: text/plain` (определяет `Content-Type` ответа, если сообщение найдено)
*   **Ответы:**
    *   `200 OK`: Сообщение успешно получено.
        *   `Content-Type` будет соответствовать заголовку `Accept` запроса (по умолчанию `text/plain`, если `Accept` не указан или содержит `*/*`).
        *   Тело ответа: содержимое сообщения из очереди.
    *   `204 No Content`: В очереди нет сообщений.
    *   `401 Unauthorized`: Неверный или отсутствующий токен авторизации.
    *   `500 Internal Server Error`: Внутренняя ошибка сервера.

**Пример (curl):**
```
curl -X GET "http://localhost:5000/get/my_test_queue" \
    -H "Authorization: Bearer YOUR_SUPER_SECRET_TOKEN" \
    -H "Accept: application/json"

curl -X GET "http://localhost:5000/get/another_queue" \
    -H "Authorization: Bearer YOUR_SUPER_SECRET_TOKEN" \
    -H "Accept: text/plain"
```

## Разработка и Тестирование

*   **Unit-тесты:** (Не добавлены в этом примере, но рекомендуется покрывать `RabbitService` и `AuthMiddleware` юнит-тестами).
*   **Интеграционное тестирование:** Можно использовать `WebApplicationFactory` для тестирования эндпоинтов вместе с реальным (или mock) RabbitMQ.

## Возможные улучшения и TODO

*   [ ] Добавить Unit-тесты для `RabbitService` и `AuthMiddleware`.
*   [ ] Реализовать более гибкое управление параметрами очереди при ее создании (TTL, max-length и т.д.) через конфигурацию или параметры запроса.
*   [ ] Добавить возможность получения нескольких сообщений за один запрос.
*   [ ] Рассмотреть использование пула каналов (`IModel`) в `RabbitService` для повышения производительности в высоконагруженных сценариях.
*   [ ] Добавить опциональное ограничение по IP-адресам.
*   [ ] Улучшить обработку ошибок и возвращать более детализированные коды ошибок в некоторых случаях.
*   [ ] Добавить Health Check эндпоинт для проверки состояния API и подключения к RabbitMQ.

## Лицензия

Проект распространяется под лицензией MIT. См. файл `LICENSE` для получения дополнительной информации. (Если вы не добавите файл LICENSE, можно убрать эту строчку или просто указать "MIT License").
