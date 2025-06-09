## Description

RabbitMQ-HttpApi � ��� ���-������ �� .NET 8, ��������������� HTTP API ��� �������������� � RabbitMQ. ��������� ������������ ��� �������� ������������� � ����������, ����������� REST-��������� ��� ������ � ��������� RabbitMQ.

- ������� �� .NET 8
- ������ ��������� ����� ���������� ���������
- HTTP API �������� �� ����� 5000

---

## Repository Overview

### ������� �����

1. **������ ����� Docker Compose:**
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

2. **���������� ���������:**
- `RabbitMQ__Host` � ����� RabbitMQ
- `RabbitMQ__Port` � ���� RabbitMQ (�� ��������� 5672)
- `RabbitMQ__Username` � ��� ������������ RabbitMQ
- `RabbitMQ__Password` � ������ RabbitMQ
- `RabbitMQ__VirtualHost` � ����������� ���� RabbitMQ
- `Api__AuthToken` � ����� ����������� ��� API
- `Api__Port` � ����, �� ������� ����������� HTTP API (�� ��������� 5000)

3. **������ � API:**
   - ����� ������� ������ ����� �������� �� ������:  
     `http://localhost:5000`

---

**����������:**  
���������, ��� ���� 5000 ��������� ������ � �� ����� ������� ��������� �� ����� ������.
