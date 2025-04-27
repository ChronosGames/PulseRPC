openssl genrsa 2048 > server.key
openssl req -new -sha256 -key server.key -out server.csr -subj "/C=CN/ST=Xiamen/L=Xiamen/O=ChronosGames Whot Demo/OU=Dev/CN=*.example.com"
openssl x509 -req -in server.csr -signkey server.key -out server.crt -days 7300 -extensions server
