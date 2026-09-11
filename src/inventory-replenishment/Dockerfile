# SPDX-License-Identifier: Apache-2.0
FROM python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates && rm -rf /var/lib/apt/lists/*
ARG MUNARIUM_REV=bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3
RUN git init /opt/munarium && cd /opt/munarium && git remote add origin https://github.com/iokaio/munarium.git && git fetch --depth 1 origin ${MUNARIUM_REV} && git checkout --detach FETCH_HEAD
COPY requirements.lock /tmp/requirements.lock
RUN pip install --no-cache-dir -r /tmp/requirements.lock && pip install --no-deps --no-build-isolation /opt/munarium/clients/python /opt/munarium/clients/matrix-python
RUN apt-get update && apt-get install -y --no-install-recommends python3-pil fonts-dejavu-core && rm -rf /var/lib/apt/lists/*
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PYTHONPATH=/app
WORKDIR /app
COPY inventory_demo /app/inventory_demo
COPY shapes /app/shapes
COPY runbooks /app/runbooks
COPY matrix /app/matrix
COPY tests /app/tests
COPY pytest.ini /app/pytest.ini
COPY ruff.toml /app/ruff.toml
COPY support.py /app/support.py
ENTRYPOINT ["python", "-m", "inventory_demo"]
