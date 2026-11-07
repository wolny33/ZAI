# ZAI 26Z – measurement series

A web application that collects measurements from IoT sensors in several series and presents them
over time. Sensors send their results straight to the REST API, each authenticated with its own API key;
an administrator manages the series and registers the sensors.

## Repository layout

| Folder | Contents |
|---|---|
| `backend/` | REST API: ASP.NET Core (.NET 10), minimal API |
| `db/` | Database schema (`schema.sql`, PostgreSQL with TimescaleDB) |
| `generator/` | Sensor emulator and sample data script (Python), see [generator/README.md](generator/README.md) |
| `task/` | **Course materials provided by the instructor, copied without changes:** the API contract (`zai-api-26z.yaml`) and the automated test script (`zai-tests.mjs`). They are the task, not part of the solution. |

## Running locally

```
cp .env.example .env        # then set your own passwords
docker compose up -d --build
```

The API is then available at `http://127.0.0.1:8080`. Sample data:

```
python generator/seed_sample_data.py --url http://127.0.0.1:8080
```

The instructor's tests (Node.js 20+):

```
node task/zai-tests.mjs --app http://127.0.0.1:8080 --user admin --pass '<admin password>' --stage E1
```
