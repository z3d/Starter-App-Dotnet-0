# The dev Keycloak with the committed realm copied in, for environments that cannot bind-mount
# the Realms directory (a container app). Same image and digest as the local container in
# Program.cs; bump both together.
FROM quay.io/keycloak/keycloak:26.4@sha256:9409c59bdfb65dbffa20b11e6f18b8abb9281d480c7ca402f51ed3d5977e6007
COPY *.json /opt/keycloak/data/import/
