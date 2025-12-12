import express from "express";
import cors from "cors";
const app = express();
app.use(express.json());
app.use(cors());
app.use(express.static('../../Assets/WebRoot/'))
app.listen(42066);
