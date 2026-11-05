#!/usr/bin/env python3
"""
Gerador de carga para o RiskGuard: envia webhooks de pedido SINTETICOS e ASSINADOS (HMAC) como a Shopify/Nuvemshop fazem.

Uso (lojista ja cadastrado e loja conectada em Configuracoes com um segredo de webhook que voce escolheu):

  python scripts/load-webhooks.py --tenant <tenantId> --secret <segredo-do-webhook> --count 2000 --concurrency 20

O segredo pode vir da variavel de ambiente WEBHOOK_SECRET (evita aparecer no historico do terminal).

Mistura de pedidos (com o limite de risco alto do lojista em 60, ver o guia):
  ~70% risco baixo   e-mail normal, valor baixo
  ~15% risco medio   e-mail com termo suspeito, valor baixo (score 50)
  ~10% risco alto    e-mail suspeito + valor >= 10.000 (score 70)
  ~ 5% valor alto    so valor >= 5.000 (score 10, continua baixo)

Com --source dummyjson os pedidos vem da API publica https://dummyjson.com (carrinhos = valor do pedido, usuarios = e-mail do
comprador); uma parte dos compradores (--suspicious-pct) ganha e-mail suspeito ou descartavel para exercitar as regras de risco.
Use --dry-run para ver a mistura esperada sem enviar nada.

Os dados sao ficticios (e-mails @exemplo-teste.com.br ou da DummyJSON). Nada e enviado a plataformas reais: sem token de acesso na conexao, o
RiskGuard so avalia (e, sem auto-bloqueio, nao age).
"""
import argparse
import base64
import hashlib
import hmac
import json
import os
import random
import statistics
import sys
import threading
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone

NORMAL_NAMES = ["ana", "bruno", "carla", "diego", "elisa", "fabio", "gabi", "hugo", "iara", "joao", "karen", "lucas", "marina", "nuno"]
SUSPICIOUS_TERMS = ["fraude", "golpe", "scam"]


def sign(platform: str, body: bytes, secret: str) -> tuple[str, str]:
    digest = hmac.new(secret.encode(), body, hashlib.sha256).digest()
    if platform == "nuvemshop":
        return "x-linkedstore-hmac-sha256", digest.hex()
    return "X-Shopify-Hmac-SHA256", base64.b64encode(digest).decode()


def build_payload(platform: str, order_id: int, email: str, amount: float) -> bytes:
    now = datetime.now(timezone.utc).isoformat()
    if platform == "nuvemshop":
        payload = {"id": order_id, "contact_email": email, "total": f"{amount:.2f}", "created_at": now.replace("+00:00", "+0000")}
    else:
        payload = {"id": order_id, "email": email, "total_price": f"{amount:.2f}", "created_at": now}
    return json.dumps(payload).encode()


def fetch_json(url: str) -> dict:
    try:
        # algumas APIs publicas recusam o User-Agent padrao do Python (403)
        request = urllib.request.Request(url, headers={"User-Agent": "RiskGuard-load-test/1.0", "Accept": "application/json"})
        with urllib.request.urlopen(request, timeout=20) as response:
            return json.load(response)
    except Exception as error:
        raise SystemExit(f"Nao consegui acessar {url}: {error}") from error


def dummyjson_orders(rng: random.Random, count: int, suspicious_pct: float) -> list[tuple[str, float]]:
    """Pedidos (e-mail, valor) a partir da DummyJSON: carrinho -> valor, usuario do carrinho -> e-mail do comprador."""
    users = {u["id"]: u["email"] for u in fetch_json("https://dummyjson.com/users?limit=0&select=email")["users"]}
    carts = fetch_json("https://dummyjson.com/carts?limit=0&select=total,userId")["carts"]
    pairs = [(users[c["userId"]], float(c["total"])) for c in carts if c["userId"] in users]
    if not pairs:
        raise SystemExit("A DummyJSON nao devolveu carrinhos/usuarios utilizaveis.")
    orders = []
    for i in range(count):
        email, amount = pairs[i % len(pairs)] if i < len(pairs) else rng.choice(pairs)
        if rng.random() < suspicious_pct / 100:
            local, domain = email.split("@", 1)
            # metade com termo suspeito no endereco, metade com dominio descartavel: as duas regras de e-mail
            email = f"{rng.choice(SUSPICIOUS_TERMS)}.{local}@{domain}" if rng.random() < 0.5 else f"{local}@mailinator.com"
        orders.append((email, round(amount, 2)))
    return orders


def expected_level(email: str, amount: float, threshold: int) -> str:
    """Espelha as regras do RiskGuard (e-mail 50, valor >= 5.000 +10, >= 10.000 +20) para prever a mistura."""
    local, _, domain = email.lower().rpartition("@")
    score = 50 if any(t in local for t in SUSPICIOUS_TERMS + ["fraud", "golpista"]) or domain.endswith("mailinator.com") else 0
    score += 20 if amount >= 10_000 else 10 if amount >= 5_000 else 0
    if score > threshold:  # backend: Alto = score ESTRITAMENTE maior que o limite
        return "alto"
    return "medio" if 0 < score >= threshold // 2 else "baixo"


def make_order(platform: str, rng: random.Random, customers: int) -> bytes:
    kind = rng.random()
    order_id = rng.randint(10**12, 10**15)

    if kind < 0.70:  # baixo
        email = f"{rng.choice(NORMAL_NAMES)}{rng.randint(1, customers)}@exemplo-teste.com.br"
        amount = round(rng.uniform(25, 900), 2)
    elif kind < 0.85:  # medio
        email = f"{rng.choice(SUSPICIOUS_TERMS)}{rng.randint(1, 500)}@exemplo-teste.com.br"
        amount = round(rng.uniform(40, 900), 2)
    elif kind < 0.95:  # alto
        email = f"{rng.choice(SUSPICIOUS_TERMS)}{rng.randint(1, 500)}@exemplo-teste.com.br"
        amount = round(rng.uniform(10_000, 18_000), 2)
    else:  # valor alto, e-mail normal
        email = f"{rng.choice(NORMAL_NAMES)}{rng.randint(1, customers)}@exemplo-teste.com.br"
        amount = round(rng.uniform(5_000, 9_500), 2)

    return build_payload(platform, order_id, email, amount)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--url", default="http://localhost:5080", help="URL base da API (padrao http://localhost:5080)")
    ap.add_argument("--tenant", required=True, help="tenantId do lojista")
    ap.add_argument("--secret", default=os.environ.get("WEBHOOK_SECRET"), help="segredo do webhook da conexao (ou WEBHOOK_SECRET)")
    ap.add_argument("--platform", choices=["shopify", "nuvemshop"], default="shopify")
    ap.add_argument("--count", type=int, default=1000, help="total de pedidos")
    ap.add_argument("--concurrency", type=int, default=10, help="envios simultaneos")
    ap.add_argument("--rps", type=float, default=0, help="limite de pedidos por segundo (0 = o mais rapido possivel)")
    ap.add_argument("--customers", type=int, default=300, help="quantos compradores distintos (e-mails normais)")
    ap.add_argument("--source", choices=["synthetic", "dummyjson"], default="synthetic", help="origem dos pedidos (padrao synthetic)")
    ap.add_argument("--suspicious-pct", type=float, default=15, help="(dummyjson) %% de compradores com e-mail suspeito/descartavel")
    ap.add_argument("--threshold", type=int, default=60, help="limite de risco alto do lojista, so para a previsao do --dry-run")
    ap.add_argument("--dry-run", action="store_true", help="mostra amostras e a mistura prevista, sem enviar nada")
    ap.add_argument("--seed", type=int, default=None, help="semente para repetir a mesma carga")
    args = ap.parse_args()

    if not args.secret and not args.dry_run:
        print("Informe o segredo com --secret ou WEBHOOK_SECRET.", file=sys.stderr)
        return 2

    rng = random.Random(args.seed)
    if args.source == "dummyjson":
        orders = dummyjson_orders(rng, args.count, args.suspicious_pct)
        bodies = [build_payload(args.platform, rng.randint(10**12, 10**15), email, amount) for email, amount in orders]
    else:
        bodies = [make_order(args.platform, rng, args.customers) for _ in range(args.count)]

    if args.dry_run:
        mix: dict[str, int] = {}
        for body in bodies:
            data = json.loads(body)
            email = data.get("email") or data.get("contact_email")
            amount = float(data.get("total_price") or data.get("total"))
            level = expected_level(email, amount, args.threshold)
            mix[level] = mix.get(level, 0) + 1
        print("Amostras:")
        for body in bodies[:3]:
            print(" ", body.decode())
        print(f"Mistura prevista (limite {args.threshold}):", ", ".join(f"{k} {v} ({v / len(bodies):.0%})" for k, v in sorted(mix.items())))
        return 0
    endpoint = f"{args.url.rstrip('/')}/api/v1/webhooks/{args.platform}/{args.tenant}"

    lock = threading.Lock()
    results: dict[int, int] = {}
    latencies: list[float] = []
    retry_after: list[int] = []
    interval = 1.0 / args.rps if args.rps > 0 else 0.0
    started = time.perf_counter()

    def send(index: int) -> None:
        if interval:
            delay = started + index * interval - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
        body = bodies[index]
        header, signature = sign(args.platform, body, args.secret)
        request = urllib.request.Request(endpoint, data=body, method="POST", headers={"Content-Type": "application/json", header: signature})
        t0 = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                status = response.status
        except urllib.error.HTTPError as error:
            status = error.code
            if error.code == 429 and error.headers.get("Retry-After", "").isdigit():
                with lock:
                    retry_after.append(int(error.headers["Retry-After"]))
        except Exception:
            status = 0  # sem resposta (rede, timeout)
        elapsed = (time.perf_counter() - t0) * 1000
        with lock:
            results[status] = results.get(status, 0) + 1
            latencies.append(elapsed)

    print(f"Enviando {args.count} pedidos ({args.platform}) para {endpoint} com {args.concurrency} em paralelo...")
    with ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        list(pool.map(send, range(args.count)))
    total = time.perf_counter() - started

    latencies.sort()
    pct = lambda p: latencies[min(len(latencies) - 1, int(len(latencies) * p))]
    print(f"\nTempo total: {total:.1f}s   vazao: {args.count / total:.1f} req/s")
    print(f"Latencia (ms): media {statistics.mean(latencies):.0f}   p50 {pct(0.50):.0f}   p95 {pct(0.95):.0f}   p99 {pct(0.99):.0f}   max {latencies[-1]:.0f}")
    print("Respostas por status:")
    meaning = {202: "aceito", 400: "requisicao invalida", 401: "assinatura invalida", 404: "tenant/conexao inexistente", 409: "duplicado", 413: "corpo grande", 429: "limite de taxa", 0: "sem resposta"}
    for status in sorted(results):
        print(f"  {status:>3}  {results[status]:>6}  {meaning.get(status, '')}")
    if retry_after:
        print(f"  (429 pediu aguardar ate {max(retry_after)}s; para o teste, suba os limites: ver o guia)")
    print("\n202 significa que o webhook foi gravado e enfileirado: o calculo de risco roda em segundo plano (Hangfire).")
    return 0 if results.get(202, 0) > 0 else 1


if __name__ == "__main__":
    sys.exit(main())
