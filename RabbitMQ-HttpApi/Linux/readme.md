# ��� ����������� � ��������� ����������

## ��� ���������

```bash
sudo nano /etc/systemd/system/rabbitmq-httpapi.service
sudo systemctl daemon-reload
sudo systemctl start rabbitmq-httpapi.service
sudo systemctl status rabbitmq-httpapi.service
```

## �������� �������

**������� 1: `ExecStart=/opt/rabbitmq-httpapi/RabbitMQ-HttpApi`**

*   **��� ��� ��������:**
    *   ���� ������� ������������, ��� �� ������������ ���� ���������� ��� **��������������� (self-contained)**.
    *   ��� ��������������� ���������� ��������� ����������� ���� (� ������ ������ `RabbitMQ-HttpApi` ��� ���������� ��� Linux), ������� �������� � ���� ����� ���������� .NET � ��� ����������� ������ ����������.
    *   ���� ����������� ���� ����� ��������� ��������, ��� ����� ������ �������� ��������� Linux.
*   **��� ����� ������� ��� ����������:**
    ��� ���������� ������� `dotnet publish` ����� ������� ��������������� �����:
    ```bash
    dotnet publish ./RabbitMQ-HttpApi/RabbitMQ-HttpApi.csproj --configuration Release --output /opt/rabbitmq-httpapi -r linux-x64 --self-contained true
    ```
    ���, ���� �� ������, ����� ����������� ���� ��� ���� (������� DLL ������ � ����, ��� .NET 6+ ��� �� ��������� ��� `--self-contained true` � ���������� ��� ����������� RID):
    ```bash
    dotnet publish ./RabbitMQ-HttpApi/RabbitMQ-HttpApi.csproj --configuration Release --output /opt/rabbitmq-httpapi -r linux-x64 --self-contained true /p:PublishSingleFile=true
    ```
    *   `-r linux-x64` (Runtime Identifier - RID): ���������, ��� ����� ������� ��������� ���������� ��������������� ���������� (��������, `linux-x64`, `linux-arm64`, `win-x64`).
    *   `--self-contained true`: �������� ���� ��� �������� ���������������� ����������.
    *   `/p:PublishSingleFile=true` (�����������): ����������� ��� � ���� ����������� ����. ��� ���� � ��� ����� ����������� ���� � ����� ��� ����������� DLL.
*   **������������:**
    *   **�������� �������������:** �� ������� ������� �� ����������� ������ ���� ���������� .NET SDK ��� Runtime (���� ������ .NET � ���������� ��������� ��� ����� ���, ��� ���� �� �������, ��� framework-dependent ��� �� ���). ��� self-contained � .NET Runtime �� ����� �� ������� ������.
    *   **��������:** ���������� ���������� �� ������ .NET, � ������� ���� ��������������, ��� ��������� �������� �������������.
*   **����������:**
    *   **������� ������ ����������:** ��� ��� ����� ���������� .NET �������� � �����, ��������� ����� ������.
    *   ���� � ��� ����� .NET ���������� �� ����� �������, ������ ����� ����� ���� ����� ����� ���������� (���� ��� self-contained).

**������� 2: `ExecStart=/usr/bin/dotnet /opt/rabbitmq-httpapi/RabbitMQ-HttpApi.dll`**

*   **��� ��� ��������:**
    *   ���� ������� ������������, ��� �� ������������ ���� ���������� ��� **��������� �� ���������� (framework-dependent)**.
    *   ��� ����� ���������� ��������� `*.dll` ���� ������ ���������� (��������, `RabbitMQ-HttpApi.dll`) � ������ ��������� DLL.
    *   ��� ������� ����� ���������� �� ������� ������� **������ ���� ����������� ��������������� (��� ����� ����� �����������) ������ .NET Runtime**.
    *   ������� `dotnet your_app.dll` ���������� ��������� ������������� `dotnet` ��� ������� ����� DLL.
*   **��� ����� ������� ��� ����������:**
    ��� ��������� �� ��������� ��� `dotnet publish`, ���� �� ������ `--self-contained true`:
    ```bash
    dotnet publish ./RabbitMQ-HttpApi/RabbitMQ-HttpApi.csproj --configuration Release --output /opt/rabbitmq-httpapi
    ```
*   **������������:**
    *   **������� ������ ����������:** ����� �������� ������ ��� ������ ���������� � ��� ������ �����������, ��� ��� ����� ���������� .NET ������������ ���������.
    *   ���� �� ������� ��������� .NET ����������, ��� ����� ������������ ���� � �� �� ������������� ����� ���������� .NET (�������� �����, ���������������� ���������� Runtime).
*   **����������:**
    *   **����������� �� �������������� .NET Runtime:** �� ������� ������ ���� ����������� ������ ������ .NET Runtime. ���� �� ��� ��� ������ ������������, ���������� �� ����������.
    *   ������������� ��������, ���� ��������� .NET Runtime ����������� �� ������������� ������ (���� .NET ��������� ������������ �������� �������������).

**��� � ����� ������ ����������� ��� ������?**

**��� ������ CI/CD workflow (`deploy-self-hosted.yml`), ��� ������ � ���������� ���������� �� ����� self-hosted runner'� (������� �������):**

*   **���� �� ����������� `dotnet publish ... --self-contained true -r linux-x64 ...` � workflow:**
    ����� � ����� `rabbitmq-httpapi.service` **���������� �����:**
    `ExecStart=/opt/rabbitmq-httpapi/RabbitMQ-HttpApi`
    (� �� �������� `sudo chmod +x /opt/rabbitmq-httpapi/RabbitMQ-HttpApi` � workflow ����� ����������, ���� ����� �� ���������� �� ������������ �������������).

*   **���� �� ����������� `dotnet publish ...` (��� `--self-contained true`) � workflow:**
    ����� � ����� `rabbitmq-httpapi.service` **���������� �����:**
    `ExecStart=/usr/bin/dotnet /opt/rabbitmq-httpapi/RabbitMQ-HttpApi.dll`
    (� ���������, ��� �� ������� ���������� .NET Runtime ������ ������).

**������������ ��� ������ ������ (self-hosted runner �� ������� �������):**

��� ������� �������. ����� ������� �� ����� ������������ � �������� ���������� .NET Runtime �� �������.

1.  **Framework-dependent (������ ����� `dotnet myapp.dll`):**
    *   **����� ������� `dotnet publish`** � workflow (������ ������).
    *   **������ ������ ����������**, ������� runner ����� ����������/��������� �� �����.
    *   **�������, ����� .NET Runtime ��� ���������� � ������������� �� �������.** ��������� runner ��� �������� �� ���� ������� �, ������ �����, .NET SDK ��� ��� ���� (��� ����� ������), �� � Runtime ��� �����. ��� ������ ������ ������� �������� ��������������� � ����� ������.
    *   ���� �� ���������� .NET � �������, ��� ����� ����� �������� � .NET Runtime �� �������.

2.  **Self-contained (������ ������� ������������ ����� `myapp`):**
    *   **����� ������� ������� `dotnet publish`** (����� ������� RID � `--self-contained`).
    *   **������ ������ ����������.**
    *   **������ ������������ �� ����, ��� ����������� �� �������** (� ����� .NET Runtime ��� *����� �����������* ����������).
    *   ����� ���� ����������������, ���� �� ������ ����� �������������� ������ Runtime ��� ������� ���������� � �������� ����������, ��� ���� ��������� ����������� Runtime ����������.

**��� � �� ������ ��� ������ `deploy-self-hosted.yml`?**

��������, ��� .NET SDK ��� ������ ���� �� self-hosted runner'� ��� ���������� ����� ������, **������� � framework-dependent ����������� � �������� ����� `dotnet YourApp.dll` �������� ������� ����� � �������� ��� ����� ����������� ��������.**

**���� �� �������� framework-dependent:**

*   **Workflow `deploy-self-hosted.yml` (��� ����������):**
    ```yaml
    - name: Publish application
      run: dotnet publish ${{ env.PROJECT_PATH }} --configuration Release --output ${{ env.DEPLOY_PATH }} --no-build
    ```
*   **���� `rabbitmq-httpapi.service`:**
    ```ini
    ExecStart=/usr/bin/dotnet /opt/rabbitmq-httpapi/RabbitMQ-HttpApi.dll
    ```
    (���������, ��� `RabbitMQ-HttpApi.dll` � ��� ���������� ��� ����� �������� DLL ����� ����������. ������ ��� ��������� � ������ �������).

���� �� �������� self-contained, ��������������� ������� �������� ������� `publish` � `ExecStart`.

**�����:** �������� ���� ������ � ��������������� ��� ��� � ������� `dotnet publish` ������ workflow, ��� � � `ExecStart` ������ ����� `systemd`. �������������� �������� � ����, ��� ������ �� ������ �����������.