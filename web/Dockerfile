FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.27-alpine
# Templates in this folder are run through envsubst at container start (official nginx image behaviour).
COPY nginx.conf /etc/nginx/templates/default.conf.template
ENV FRAME_ANCESTORS="'none'"
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
