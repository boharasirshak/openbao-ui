ui = true
disable_mlock = true

listener "tcp" {
  address = "0.0.0.0:8200"
  tls_disable = 1
}

storage "raft" {
  path = "/openbao/data"
  node_id = "dev-1"
}
