#!/usr/bin/env bash
set -euo pipefail

# Generate development TLS certificate/key for login-service.
# Output:
#   certs/login-cert.pem
#   certs/login-key.pem
#
# SANs:
#   DNS: localhost
#   IP : 127.0.0.1

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CERT_DIR="${ROOT_DIR}/certs"
CONF_PATH="${CERT_DIR}/openssl-server.cnf"
KEY_PATH="${CERT_DIR}/login-key.pem"
CERT_PATH="${CERT_DIR}/login-cert.pem"

mkdir -p "${CERT_DIR}"

cat > "${CONF_PATH}" <<'EOF'
[req]
default_bits = 2048
prompt = no
default_md = sha256
distinguished_name = dn
x509_extensions = v3_req

[dn]
CN = localhost

[v3_req]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
IP.1 = 127.0.0.1
EOF

openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout "${KEY_PATH}" \
  -out "${CERT_PATH}" \
  -config "${CONF_PATH}"

echo "Generated:"
echo "  ${KEY_PATH}"
echo "  ${CERT_PATH}"
echo
echo "Certificate summary:"
openssl x509 -in "${CERT_PATH}" -noout -text | rg "CA:|Extended Key Usage|Subject Alternative Name" || true
