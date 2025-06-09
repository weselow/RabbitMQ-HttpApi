## Description

RabbitMQ-HttpApi Ч это веб-сервис на .NET 8, предоставл€ющий HTTP API дл€ взаимодействи€ с RabbitMQ.  онтейнер предназначен дл€ быстрого развертывани€ и интеграции, обеспечива€ REST-интерфейс дл€ работы с очеред€ми RabbitMQ.

- ќснован на .NET 8
- √ибка€ настройка через переменные окружени€
- HTTP API доступен на порту 5000

---

## Repository Overview

### Ѕыстрый старт

1. **«апуск через Docker Compose:**
```
   services:
     rabbitmq-httpapi:
       image: <your-dockerhub-username>/rabbitmq-httpapi:latest
       ports:
         - "5000:5000"
       environment:
         RabbitMQ__Host: rabbitmq
         RabbitMQ__Port: 5672
         RabbitMQ__Username: guest
         RabbitMQ__Password: guest
         RabbitMQ__VirtualHost: /
         Api__AuthToken: YOUR_SUPER_SECRET_TOKEN
         Api__Port: 5000
```

2. **ѕеременные окружени€:**
- `RabbitMQ__Host` Ч адрес RabbitMQ
- `RabbitMQ__Port` Ч порт RabbitMQ (по умолчанию 5672)
- `RabbitMQ__Username` Ч им€ пользовател€ RabbitMQ
- `RabbitMQ__Password` Ч пароль RabbitMQ
- `RabbitMQ__VirtualHost` Ч виртуальный хост RabbitMQ
- `Api__AuthToken` Ч токен авторизации дл€ API
- `Api__Port` Ч порт, на котором запускаетс€ HTTP API (по умолчанию 5000)

3. **ƒоступ к API:**
   - ѕосле запуска сервис будет доступен по адресу:  
     `http://localhost:5000`

---

**ѕримечание:**  
”бедитесь, что порт 5000 проброшен наружу и не зан€т другими сервисами на вашей машине.
