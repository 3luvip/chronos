#!/bin/bash

BASE=~/Downloads/Nro/chronos_sever

cd "$BASE" || exit
cargo build || exit

# Gateway
kitty bash -c "cd $BASE && cargo run -p gateway; exec bash" &

# Login
kitty bash -c "cd $BASE && cargo run -p login-service; exec bash" &

# Game
kitty bash -c "cd $BASE && cargo run -p game_server; exec bash" &