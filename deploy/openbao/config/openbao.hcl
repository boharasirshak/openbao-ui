ui = true
disable_mlock = true
api_addr = "http://openbao:8200"
cluster_addr = "http://openbao:8201"

listener "tcp" {
  address = "0.0.0.0:8200"
  cluster_address = "0.0.0.0:8201"
  tls_disable = 1
}

storage "raft" {
  path = "/openbao/file"
  node_id = "dev-1"
}
